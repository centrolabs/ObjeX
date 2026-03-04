namespace ObjeX.Core.Interfaces;

public interface IHasTimestamps
{
    public DateTime CreatedAt { get; init; } 
    public DateTime UpdatedAt { get; set; }
}