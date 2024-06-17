using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hipbotnator.TwitchEventSubWebsocket.Types.Event
{
    public class ChannelFollowEventArgs : EventArgs
    {
        public User User { get; }
        public User Broadcaster { get; }
        public DateTime FollowedAt { get; }

        public ChannelFollowEventArgs(string userID, string userLogin, string userDisplayname, string broadcasterID, string broadcasterLogin, 
            string broadcasterDisplayname, DateTime followedAt)
        {
            User = new User(userID, userLogin, userDisplayname);
            Broadcaster = new User(broadcasterID, broadcasterLogin, broadcasterDisplayname);
            FollowedAt = followedAt;
        }
    }
}
