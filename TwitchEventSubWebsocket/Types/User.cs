using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hipbotnator.TwitchEventSubWebsocket.Types
{
    public class User
    {
        public string ID { get; }
        public string Login { get; }
        public string Displayname { get; }

        public User (string id, string login, string displayname)
        {
            ID = id;
            Login = login;
            Displayname = displayname;
        }
    }
}
