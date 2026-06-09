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
        private readonly IConfiguration _configuration;

        public EmailService(IOptions<EmailSettings> settings, IConfiguration configuration)
        {
            _settings = settings.Value;
            _configuration = configuration;
        }

        private string GetBaseUrl()
        {
            return _configuration["AppBaseUrl"] ?? "https://localhost:7057";
        }

        private string GetCompanyName()
        {
            return _configuration["CompanyName"] ?? "Humatrix HRMS";
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


        public async Task SendRehireNotificationAsync(
          string email,
          string employeeName,
          DateTime rehireDate,
          string department,
          string designation,
          string? remarks)
        {
            var baseUrl = GetBaseUrl();
            var companyName = GetCompanyName();

            var subject = $"Welcome Back to {companyName} - You've Been Rehired";

            var body = $@"
        <html>
        <head>
            <style>
                body {{ font-family: Arial, sans-serif; }}
                .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                .header {{ background-color: #28a745; color: white; padding: 20px; text-align: center; }}
                .content {{ padding: 20px; background-color: #f8f9fa; }}
                .details {{ background-color: white; padding: 15px; margin: 15px 0; border-radius: 5px; }}
                .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #6c757d; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h2>Welcome Back, {employeeName}!</h2>
                </div>
                <div class='content'>
                    <p>Dear {employeeName},</p>
                    
                    <p>We are pleased to inform you that you have been <strong>rehired</strong> effective <strong>{rehireDate:dd MMM yyyy}</strong>.</p>
                    
                    <div class='details'>
                        <h3>📋 Rehire Details</h3>
                        <p><strong>Department:</strong> {department}</p>
                        <p><strong>Designation:</strong> {designation}</p>
                        <p><strong>Effective Date:</strong> {rehireDate:dd MMM yyyy}</p>
                        {(remarks != null ? $"<p><strong>Remarks:</strong> {remarks}</p>" : "")}
                    </div>
                    
                    <div class='details'>
                        <h3>🔐 Account Access</h3>
                        <p>Your existing account has been reactivated. You can login using your <strong>existing email and password</strong>.</p>
                        <p><strong>Login URL:</strong> <a href='{baseUrl}/login'>{baseUrl}/login</a></p>
                        <p>If you have forgotten your password, please use the 'Forgot Password' option on the login page.</p>
                    </div>
                    
                    <div class='details'>
                        <h3>📝 Next Steps</h3>
                        <ul>
                            <li>Login to your account</li>
                            <li>Review and update your profile information</li>
                            <li>Check your assigned assets</li>
                            <li>Review your leave balance</li>
                            <li>Contact HR if you need any assistance</li>
                        </ul>
                    </div>
                    
                    <p>We are excited to have you back on the team!</p>
                    
                    <p>Best regards,<br />
                    <strong>HR Team</strong></p>
                </div>
                <div class='footer'>
                    <p>This is an automated message. Please do not reply to this email.</p>
                </div>
            </div>
        </body>
        </html>";

            await SendEmailAsync(email, subject, body);
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