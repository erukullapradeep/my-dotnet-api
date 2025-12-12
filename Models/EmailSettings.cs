namespace rfq_api.Models
{
    public class EmailSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string AdminEmail { get; set; } = "";
    }
}
