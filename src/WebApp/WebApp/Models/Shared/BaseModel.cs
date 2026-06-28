namespace WebApp.Models.Shared;

public class BaseModel 
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; protected set; }

    protected void ConfigureForEdit(Guid id, DateTime createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
        UpdatedAt = DateTime.UtcNow;
    }

    protected void MarkAsUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }
    
}