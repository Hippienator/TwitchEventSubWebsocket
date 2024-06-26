using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;
using TwitchEventSubWebsocket.Communication;
using TwitchEventSubWebsocket.Types.Event;
using System.Collections.Generic;
using Timer = System.Timers.Timer;
using TwitchEventSubWebsocket.Types;
using TwitchEventSubWebsocket.SubcriptionHandling;

namespace TwitchEventSubWebsocket
{
    public class EventSubWebsocket
    {
        /// <summary>
        /// The ID of the websocket connection.
        /// </summary>
        public string? SessionID 
        {
            get { return sessionID; } 
            set { if (Subscribe != null) Subscribe.WebsocketID = value; sessionID = value; } 
        }

        /// <summary>
        /// Client ID associated with using the Twitch API. Only required if you want this to handle making subscriptions to the EventSub.
        /// </summary>
        public string ClientID { get; set; }

        /// <summary>
        /// The access token for the Twitch API. Only required if you want this to handle making subscriptions to the EventSub.
        /// </summary>
        public string AccessToken;

        /// <summary>
        /// Set of methods to subscribe to different topics on the EventSub.
        /// </summary>
        public SubscriptionHandler Subscribe;

        public string? sessionID;
        private WebsocketClient EventWebsocket;
        private Uri OriginalURI;
        private List<string> MessageIDsOld = new List<string>();
        private List<string> MessageIDsNew = new List<string>();
        private Timer MessageTimer = new Timer() { Interval = 600000, AutoReset = true };
        private Timer ReconnectTimer = new Timer { Interval = 10000, AutoReset = true };
        private static readonly object MessageListLock = new object();

        #region Events
        /// <summary>
        /// Fires when the websocket receives the Welcome message. Remember to add scopes through the API or the connection will close.
        /// </summary>
        public event EventHandler<ConnectedEventArgs> OnConnected;

        /// <summary>
        /// Fires when a subscription has been revoked.
        /// </summary>
        public event EventHandler<RevocationEventArgs> OnRevocation;

        /// <summary>
        /// Fires when a user follows a channel that is monitored.
        /// </summary>
        public event EventHandler<ChannelFollowEventArgs> OnChannelFollow;

        /// <summary>
        /// Fires when when a raid happens, either from a channel monitored for outgoing raids or a channel monitored for incoming raids.
        /// </summary>
        public event EventHandler<RaidEventArgs> OnRaid;

        /// <summary>
        /// Fires when a monitored channel updates its stream information.
        /// </summary>
        public event EventHandler<ChannelUpdateEventArgs> OnChannelUpdate;

        /// <summary>
        /// Fires when someone subscribes to a channel being monitored for new subscriptions. Does not fire on resubs.
        /// </summary>
        public event EventHandler<ChannelSubscribeEventArgs> OnChannelSubscribe;

        #endregion

        public EventSubWebsocket(string url, string clientID = "", string accessToken = "")
        {
            Subscribe = new SubscriptionHandler(clientID, accessToken);
            OriginalURI = new Uri(url);
            EventWebsocket = new WebsocketClient(OriginalURI);
            EventWebsocket.IsReconnectionEnabled = false;
            EventWebsocket.MessageReceived.Subscribe(m => Task.Run(() => WebsocketMessageHandler(this, m)));
            EventWebsocket.DisconnectionHappened.Subscribe(e => Task.Run(() => WebsocketLostConnection(this, e)));
            Task.Run(() => Connect(OriginalURI));
            MessageTimer.Elapsed += MessageTimer_Elapsed;
            ReconnectTimer.Elapsed += ReconnectTimer_Elapsed;
            ClientID = clientID;
            AccessToken = accessToken;
        }

        #region Timer Events

        //Timer to try reconnecting on failed connection.
        private void ReconnectTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (EventWebsocket.IsRunning)
            {
                ReconnectTimer.Stop();
                return;
            }
            EventWebsocket.Start();
        }

        //Handles the removing the storage of message ids to safeguard against replay attacks. The way it's set up message ids stay for 10-20 minutes.
        private void MessageTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (MessageListLock)
            {
                MessageIDsOld = MessageIDsNew;
                MessageIDsNew = new List<string>();
            }
        }
        #endregion

        //Automatically reconnects the websocket if connection is lost, except if disconnected on purpose.
        private void WebsocketLostConnection(object sender, DisconnectionInfo info)
        {
            MessageTimer.Stop();
            if (info.Type != DisconnectionType.ByUser)
                Connect(OriginalURI);
        }

        //Prepares everything for a fresh connection and connects to the given Uri.
        private void Connect(Uri url)
        {
            EventWebsocket.Url = url;
            MessageIDsOld = new List<string>();
            MessageIDsNew = new List<string>();
            EventWebsocket.Start();
            ReconnectTimer.Start();
        }

        //Handles incoming messages in the websocket
        private void WebsocketMessageHandler(object sender, ResponseMessage e)
        {
            if (e.MessageType != WebSocketMessageType.Text)
            {
                return;
            }

            ServerMessage msg = JsonConvert.DeserializeObject<ServerMessage>(e.Text);

            //This section handles the safeguarding against replay attacks.
            TimeSpan dtime = DateTime.Now - (DateTime)msg?.MetaData["message_timestamp"];
            if (dtime.Minutes >= 10)
                return;
            string messageID = (string)msg.MetaData["message_id"];
            if (MessageIDsNew.Contains(messageID) || MessageIDsOld.Contains(messageID))
                return;
            lock (MessageListLock)
            {
                MessageIDsNew.Add(messageID);
            }

            switch ((string)msg.MetaData["message_type"])
            {
                //Happens when first connecting
                case "session_welcome":
                    {
                        EventWebsocket.ReconnectTimeout = TimeSpan.FromSeconds((int)msg.Payload.Session["keepalive_timeout_seconds"]);
                        MessageTimer.Start();
                        SessionID = (string)msg.Payload.Session["id"];
                        OnConnected?.Invoke(this, new ConnectedEventArgs((string)msg.Payload.Session["id"], (int)msg.Payload.Session["keepalive_timeout_seconds"]));
                    }
                    break;

                //Happens when a scope the websocket is subscribed to fires.
                case "notification":
                    {
                        Task.Run(() => NotificationHanlder((string)msg.Payload.Subscription["type"], msg.Payload.Event));
                    }
                    break;

                //Happens when Twitch wants you to reconnect through a new url.
                case "session_reconnect":
                    {
                        EventWebsocket.Url = new Uri((string)msg.Payload.Session["reconnect_url"]);
                        EventWebsocket.StopOrFail(WebSocketCloseStatus.NormalClosure, "Asked to reconnect to differnt URI");
                        EventWebsocket.StartOrFail();
                    }
                    break;

                //Happens when a subscription is revoked.
                case "revocation":
                    {
                        JObject info = msg.Payload.Subscription;
                        OnRevocation?.Invoke(this, new RevocationEventArgs((string)info["id"], (string)info["status"], (string)info["type"], info["conditions"]?.ToObject<JObject>()));
                    }
                    break;

            }
        }

        private void NotificationHanlder(string notifType, JObject args)
        {
            switch (notifType)
            {
                case "channel.follow":
                    {
                        OnChannelFollow?.Invoke(this, new ChannelFollowEventArgs((string)args["user_id"], (string)args["user_login"], (string)args["user_name"], 
                            (string)args["broadcaster_user_id"], (string)args["broadcaster_user_login"], (string)args["broadcaster_user_name"], (DateTime)args["followed_at"]));
                        break;
                    }

                case "channel.raid":
                    {
                        OnRaid?.Invoke(this, new RaidEventArgs((string)args["from_broadcaster_user_id"], (string)args["from_broadcaster_user_login"], 
                            (string)args["from_broadcaster_user_name"], (string)args["to_broadcaster_user_id"], (string)args["to_broadcaster_user_login"], 
                            (string)args["to_broadcaster_user_name"], (int)args["viewers"]));
                        break;
                    }

                case "channel.update":
                    {
                        User broadcaster = new User((string)args["broadcaster_user_id"], (string)args["broadcaster_user_login"], (string)args["broadcaster_user_name"]);
                        OnChannelUpdate?.Invoke(this, new ChannelUpdateEventArgs(broadcaster, (string)args["title"], (string)args["language"], (string)args["category_id"],
                            (string)args["category_name"], args["content_classification_labels"]?.ToObject<string[]>()));
                        break;
                    }

                case "channel.subscribe":
                    {
                        User user = new User((string)args["user_id"], (string)args["user_login"], (string)args["user_name"]);
                        User broadcaster = new User((string)args["broadcaster_user_id"], (string)args["broadcaster_user_login"], (string)args["broadcaster_user_name"]);
                        OnChannelSubscribe?.Invoke(this, new ChannelSubscribeEventArgs(user, broadcaster, (int)args["tier"], (bool)args["is_gift"]));
                        break;
                    }
            }
        }

    }
}