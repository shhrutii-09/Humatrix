using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace Humatrix_HRMS.Services
{
    public class EmailSettings
    {
        public string From { get; set; } = string.Empty;
        public string SmtpServer { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class EmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }


        private async Task SendEmailAsync(
    string toEmail,
    string subject,
    string body)
        {
            using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
            {
                Credentials = new NetworkCredential(
                    _settings.Username,
                    _settings.Password),

                EnableSsl = true
            };

            var mail = new MailMessage
            {
                From = new MailAddress(_settings.From),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);
        }

        public async Task SendOrganizationInviteAsync(
            string toEmail,
            string organizationName,
            string setupLink)
        {
            var subject = $"Welcome to {organizationName}";

            var body = $@"
                <h2>Welcome to HuMatrix HRMS</h2>

                <p>Your organization account has been created.</p>

                <p>
                    Click below to setup your password:
                </p>

                <p>
                    <a href='{setupLink}'>
                        Setup Account
                    </a>
                </p>

                <br/>

                <p>
                    Thanks,<br/>
                    HuMatrix HRMS
                </p>
            ";

            //using var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
            //{
            //    Credentials = new NetworkCredential(
            //        _settings.Username,
            //        _settings.Password),

            //    EnableSsl = true
            //};

            //var mail = new MailMessage
            //{
            //    From = new MailAddress(_settings.From),
            //    Subject = subject,
            //    Body = body,
            //    IsBodyHtml = true
            //};

            //mail.To.Add(toEmail);

            //await client.SendMailAsync(mail);
            await SendEmailAsync(toEmail, subject, body);
        }

        public async Task SendEmployeeInviteAsync(
     string toEmail,
     string employeeName,
     string role,
     string setupLink,
     string invitedBy)
        {
            var subject = $"Welcome to Humatrix HRMS";

            var body = $@"
<h2>Hello {employeeName},</h2>

<p>
    You have been invited to Humatrix HRMS
    as <b>{role}</b>.
</p>

<p>
    Invited By:
    <b>{invitedBy}</b>
</p>

<p>
    Click below to setup your account:
</p>

<p>
    <a href='{setupLink}'
       style='padding:10px 18px;
              background:#5c4ac7;
              color:white;
              text-decoration:none;
              border-radius:6px;'>
        Setup Account
    </a>
</p>

<br/>

<p>Humatrix HRMS</p>
";

            await SendEmailAsync(toEmail, subject, body);
        }
    }
}