using SQLite;

namespace GymTracker.Avalonia.Models;

public class AppSetting
{
    [PrimaryKey]
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
