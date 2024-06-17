using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitchEventSubWebsocket.Types.Event
{
    public class ConnectedEventArgs : EventArgs
    {
        public string ID { get; }
        public int KeepaliveTime { get; }

        public ConnectedEventArgs (string id, int keepaliveTime)
        {
            ID = id;
            KeepaliveTime = keepaliveTime;
        }

    }
}
