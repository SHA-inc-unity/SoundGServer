namespace shooter_server
{
    public class Message
    {
        public string id_chat {  get; set; }
        public int id_sender { get; set; }
        public int id_msg { get; set; }
        public DateTime time_msg { get; set; }
        public byte[] msg { get; set; }
        public byte[] public_key { get; set; }
        public bool is_erase { get; set; }

        public Message()
        {
            msg = Array.Empty<byte>();
            public_key = Array.Empty<byte>();
        }

        public string GetString()
        {

            string base64Msg = msg.Length > 0 ? Convert.ToBase64String(msg) : "+";
            string base64PublicKey = public_key.Length > 0 ? Convert.ToBase64String(public_key) : "-";
            return $"{id_chat} {id_sender} {id_msg} {time_msg.ToString("dd.MM.yyyy HH:mm:ss")} {base64Msg} {base64PublicKey} ";
        }
    }
}