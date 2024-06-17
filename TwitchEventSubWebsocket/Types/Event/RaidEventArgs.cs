using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hipbotnator.TwitchEventSubWebsocket.Types.Event
{
    public class RaidEventArgs : EventArgs
    {
        public User Raider { get; }
        public User Broadcaster { get; }
        public int RaiderCount { get; }

        public RaidEventArgs(string raiderID, string raiderLogin, string raiderDisplayname, string broadcasterID, string broadcasterLogin, string broadcasterDisplayname, int raiderCount)
        {
            Raider = new User(raiderID, raiderLogin, raiderDisplayname);
            Broadcaster = new User(broadcasterID, broadcasterLogin, broadcasterDisplayname);
            RaiderCount = raiderCount;
        }
    }
}
