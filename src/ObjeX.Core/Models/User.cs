using Microsoft.AspNetCore.Identity;
using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class User : IdentityUser, IHasTimestamps
{
    public long StorageUsedBytes { get; set; }
    
    // IHasTimestamps
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}