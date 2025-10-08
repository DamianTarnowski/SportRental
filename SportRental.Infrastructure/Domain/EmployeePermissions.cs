using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SportRental.Infrastructure.Domain
{
    public class EmployeePermissions
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(Employee))]
        public Guid EmployeeId { get; set; }

        // Kierownik permissions
        public bool KierownikCanAddClient { get; set; } = false;
        public bool KierownikCanEditClient { get; set; } = false;
        public bool KierownikCanDeleteClient { get; set; } = false;
        public bool KierownikCanAddProduct { get; set; } = false;
        public bool KierownikCanEditProduct { get; set; } = false;
        public bool KierownikCanDeleteProduct { get; set; } = false;
        public bool KierownikCanAddRental { get; set; } = false;
        public bool KierownikCanEditRental { get; set; } = false;
        public bool KierownikCanDeleteRental { get; set; } = false;
        public bool KierownikCanAddEmployee { get; set; } = false;
        public bool KierownikCanEditEmployee { get; set; } = false;
        public bool KierownikCanDeleteEmployee { get; set; } = false;
        public bool KierownikCanSeeReports { get; set; } = false;
        public bool KierownikCanAddMultipleRental { get; set; } = false;
        public bool KierownikCanEditMultipleRental { get; set; } = false;
        public bool KierownikCanDeleteMultipleRental { get; set; } = false;

        // Manager permissions
        public bool ManagerCanAddClient { get; set; } = false;
        public bool ManagerCanEditClient { get; set; } = false;
        public bool ManagerCanDeleteClient { get; set; } = false;
        public bool ManagerCanAddProduct { get; set; } = false;
        public bool ManagerCanEditProduct { get; set; } = false;
        public bool ManagerCanDeleteProduct { get; set; } = false;
        public bool ManagerCanAddRental { get; set; } = false;
        public bool ManagerCanEditRental { get; set; } = false;
        public bool ManagerCanDeleteRental { get; set; } = false;
        public bool ManagerCanAddEmployee { get; set; } = false;
        public bool ManagerCanEditEmployee { get; set; } = false;
        public bool ManagerCanDeleteEmployee { get; set; } = false;
        public bool ManagerCanSeeReports { get; set; } = false;
        public bool ManagerCanAddMultipleRental { get; set; } = false;
        public bool ManagerCanEditMultipleRental { get; set; } = false;
        public bool ManagerCanDeleteMultipleRental { get; set; } = false;

        // Pracownik permissions
        public bool PracownikCanAddClient { get; set; } = false;
        public bool PracownikCanEditClient { get; set; } = false;
        public bool PracownikCanDeleteClient { get; set; } = false;
        public bool PracownikCanAddProduct { get; set; } = false;
        public bool PracownikCanEditProduct { get; set; } = false;
        public bool PracownikCanDeleteProduct { get; set; } = false;
        public bool PracownikCanAddRental { get; set; } = false;
        public bool PracownikCanEditRental { get; set; } = false;
        public bool PracownikCanDeleteRental { get; set; } = false;
        public bool PracownikCanAddEmployee { get; set; } = false;
        public bool PracownikCanEditEmployee { get; set; } = false;
        public bool PracownikCanDeleteEmployee { get; set; } = false;
        public bool PracownikCanSeeReports { get; set; } = false;
        public bool PracownikCanAddMultipleRental { get; set; } = false;
        public bool PracownikCanEditMultipleRental { get; set; } = false;
        public bool PracownikCanDeleteMultipleRental { get; set; } = false;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        // Navigation properties
        public Tenant? Tenant { get; set; }
        public Employee? Employee { get; set; }
    }
}
