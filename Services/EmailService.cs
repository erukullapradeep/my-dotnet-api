using MimeKit;
using MailKit.Net.Smtp;

public class EmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public void SendEmail(string to, string subject, string body)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress("RFQ System", _config["EmailSettings:Username"]));
        email.To.Add(new MailboxAddress("", to));
        email.Subject = subject;

        email.Body = new TextPart("html") { Text = body };

        using var smtp = new SmtpClient();
        smtp.Connect(_config["EmailSettings:Host"], int.Parse(_config["EmailSettings:Port"]), false);
        smtp.Authenticate(_config["EmailSettings:Username"], _config["EmailSettings:Password"]);
        smtp.Send(email);
        smtp.Disconnect(true);
    }
}
