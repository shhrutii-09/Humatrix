namespace Humatrix_HRMS.Models
{
    public class UserInvite
    {
        public int Id { get; set; }

        public string Email { get; set; }
        public string UserId { get; set; }

        public string Token { get; set; }

        public string Role { get; set; } // ✅ NEW

        public Guid? OrganizationId { get; set; } // ✅ NEW

        public bool IsUsed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}