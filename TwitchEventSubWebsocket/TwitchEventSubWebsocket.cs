using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;
using TwitchEventSubWebsocket.Communication;
using TwitchEventSubWebsocket.Types.Event;
using Hipbotnator.TwitchEventSubWebsocket.Types.Event;
using System.Collections.Generic;
using Timer = System.Timers.Timer;

namespace TwitchEventSubWebsocket
{
    public class EventSubWebsocket
    {
        private WebsocketClient EventWebsocket;
        private Uri OriginalURI;
        public string SessionID = "";
        private List<string> MessageIDsOld = new List<string>();
        private List<string> MessageIDsNew = new List<string>();
        private Timer MessageTimer = new Timer() { Interval = 600000, AutoReset = true };
        private Timer ReconnectTimer = new Timer { Interval = 10000, AutoReset = true };
        private static readonly object MessageListLock = new object();

        #region Events
        public event EventHandler<ConnectedEventArgs> OnConnected;
        public event EventHandler<RevocationEventArgs> OnRevocation;
        public event EventHandler<ChannelFollowEventArgs> OnChannelFollow;
        public event EventHandler<RaidEventArgs> OnRaid;

        #endregion

        public EventSubWebsocket(string url)
        {
            if (!url.ToLower().StartsWith("wss://"))
                url = "wss://" + url;
            OriginalURI = new Uri(url);
            EventWebsocket = new WebsocketClient(OriginalURI);
            EventWebsocket.IsReconnectionEnabled = false;
            EventWebsocket.MessageReceived.Subscribe(m => Task.Run(() => WebsocketMessageHandler(this, m)));
            EventWebsocket.DisconnectionHappened.Subscribe(e => Task.Run(() => WebsocketLostConnection(this, e)));
            Task.Run(() => Connect(OriginalURI));
            MessageTimer.Elapsed += MessageTimer_Elapsed;
            ReconnectTimer.Elapsed += ReconnectTimer_Elapsed;
        }

        #region Timer Events
        private void ReconnectTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (EventWebsocket.IsRunning)
            {
                ReconnectTimer.Stop();
                return;
            }
            EventWebsocket.Start();
        }

        private void MessageTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (MessageListLock)
            {
                MessageIDsOld = MessageIDsNew;
                MessageIDsNew = new List<string>();
            }
        }
        #endregion

        private void WebsocketLostConnection(object sender, DisconnectionInfo info)
        {
            MessageTimer.Stop();
            if (info.Type != DisconnectionType.ByUser)
                Connect(OriginalURI);
        }

        public void Connect(Uri url)
        {
            EventWebsocket.Url = url;
            MessageIDsOld = new List<string>();
            MessageIDsNew = new List<string>();
            EventWebsocket.Start();
            ReconnectTimer.Start();
        }

        private void WebsocketMessageHandler(object sender, ResponseMessage e)
        {
            if (e.MessageType != WebSocketMessageType.Text)
            {
                return;
            }

            ServerMessage msg = JsonConvert.DeserializeObject<ServerMessage>(e.Text);

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
                case "session_welcome":
                    {
                        EventWebsocket.ReconnectTimeout = TimeSpan.FromSeconds((int)msg.Payload.Session["keepalive_timeout_seconds"]);
                        MessageTimer.Start();
                        OnConnected?.Invoke(this, new ConnectedEventArgs((string)msg.Payload.Session["id"], (int)msg.Payload.Session["keepalive_timeout_seconds"]));
                    }
                    break;

                case "notification":
                    {
                        Task.Run(() => NotificationHanlder((string)msg.Payload.Subscription["type"], msg.Payload.Event));
                    }
                    break;

                case "session_reconnect":
                    {
                        EventWebsocket.Url = new Uri((string)msg.Payload.Session["reconnect_url"]);
                        EventWebsocket.StopOrFail(WebSocketCloseStatus.NormalClosure, "Asked to reconnect to differnt URI");
                        EventWebsocket.StartOrFail();
                    }
                    break;

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
            }
        }

    }
}