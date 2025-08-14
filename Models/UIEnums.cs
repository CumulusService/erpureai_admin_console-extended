namespace AdminConsole.Models;

/// <summary>
/// Defines the visual style and semantic meaning of action buttons
/// </summary>
public enum ActionType
{
    /// <summary>Primary action - most important action on the page</summary>
    Primary,
    
    /// <summary>Secondary action - supporting actions</summary>
    Secondary,
    
    /// <summary>Success action - confirming or completing something positive</summary>
    Success,
    
    /// <summary>Warning action - actions that require caution</summary>
    Warning,
    
    /// <summary>Danger action - destructive or irreversible actions</summary>
    Danger,
    
    /// <summary>Info action - informational or neutral actions</summary>
    Info,
    
    /// <summary>View action - read-only viewing operations</summary>
    View,
    
    /// <summary>Edit action - modification operations</summary>
    Edit,
    
    /// <summary>Delete action - deletion operations (uses outline style for safety)</summary>
    Delete
}

/// <summary>
/// Button size options
/// </summary>
public enum ButtonSize
{
    Small,
    Normal,
    Large
}

/// <summary>
/// User status display options
/// </summary>
public enum UserStatusDisplay
{
    Badge,
    Text,
    Icon
}

/// <summary>
/// Confirmation modal types
/// </summary>
public enum ConfirmationType
{
    Info,
    Success,
    Warning,
    Danger
}