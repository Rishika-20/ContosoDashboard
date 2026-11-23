using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key] public int DocumentActivityId { get; set; }
    public int? DocumentId { get; set; }
    [Required] public int ActorUserId { get; set; }
    [Required] public DocumentActivityType ActivityType { get; set; }
    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;
    public int? ProjectId { get; set; }
    public int? TargetUserId { get; set; }
    [MaxLength(100)] public string? TargetDepartment { get; set; }
    [Required] public DocumentActivityOutcome Outcome { get; set; }
    [MaxLength(1000)] public string? Details { get; set; }

    [ForeignKey(nameof(DocumentId))] public virtual Document? Document { get; set; }
    [ForeignKey(nameof(ActorUserId))] public virtual User Actor { get; set; } = null!;
}
