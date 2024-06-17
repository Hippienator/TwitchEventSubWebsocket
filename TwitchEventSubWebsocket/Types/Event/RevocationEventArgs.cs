using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hipbotnator.TwitchEventSubWebsocket.Types.Event
{
    public class RevocationEventArgs : EventArgs
    {
        public string SubscriptionID { get; }
        public string Status { get; }
        public string Type { get; }
        public Conditions Conditions { get; }

        public RevocationEventArgs (string subscriptionID, string status, string type, JObject conditions)
        {
            SubscriptionID = subscriptionID;
            Status = status;
            Type = type;
            Conditions = new Conditions(conditions);
        }
    }
}
