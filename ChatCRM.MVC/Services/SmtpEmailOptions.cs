namespace ChatCRM.MVC.Services
{
    public class SmtpEmailOptions
    {
        public string Host { get; set; } = "smtp.gmail.com";

        public int Port { get; set; } = 587;

        public bool EnableSsl { get; set; } = true;

        public string FromEmail { get; set; } = "hicaronacamora@gmail.com";

        public string FromName { get; set; } = "ChatCRM";

        public string? Username { get; set; }

        public string? Password { get; set; }
    }
}
