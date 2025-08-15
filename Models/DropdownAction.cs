using Microsoft.AspNetCore.Components;

namespace AdminConsole.Models;

/// <summary>
/// Represents an action item in a dropdown menu
/// </summary>
public class DropdownAction
{
    /// <summary>The main title/text for the action</summary>
    public string Title { get; set; } = "";
    
    /// <summary>Optional description explaining what this action does</summary>
    public string Description { get; set; } = "";
    
    /// <summary>Icon class (e.g., "fas fa-eye")</summary>
    public string IconClass { get; set; } = "";
    
    /// <summary>The action type that determines styling</summary>
    public ActionType Type { get; set; } = ActionType.Secondary;
    
    /// <summary>Callback to execute when clicked</summary>
    public EventCallback OnClick { get; set; }
    
    /// <summary>Callback to execute on mouse over (for preloading)</summary>
    public EventCallback OnMouseOver { get; set; }
    
    /// <summary>Whether this action is currently disabled</summary>
    public bool IsDisabled { get; set; } = false;
    
    /// <summary>Whether this action should be visible</summary>
    public bool IsVisible { get; set; } = true;
    
    /// <summary>Whether this is a separator item</summary>
    public bool IsSeparator { get; set; } = false;
    
    /// <summary>Optional text describing the consequence of this action</summary>
    public string ConsequenceText { get; set; } = "";

    /// <summary>Creates a standard view action</summary>
    public static DropdownAction CreateView(string title, EventCallback onClick, string description = "")
    {
        return new DropdownAction
        {
            Title = title,
            Description = description,
            IconClass = "fas fa-eye",
            Type = ActionType.View,
            OnClick = onClick
        };
    }

    /// <summary>Creates a standard edit action</summary>
    public static DropdownAction CreateEdit(string title, EventCallback onClick, string description = "")
    {
        return new DropdownAction
        {
            Title = title,
            Description = description,
            IconClass = "fas fa-edit",
            Type = ActionType.Edit,
            OnClick = onClick
        };
    }

    /// <summary>Creates a dangerous action with clear consequences</summary>
    public static DropdownAction CreateDanger(string title, EventCallback onClick, string description = "", string consequence = "")
    {
        return new DropdownAction
        {
            Title = title,
            Description = description,
            IconClass = "fas fa-exclamation-triangle",
            Type = ActionType.Danger,
            OnClick = onClick,
            ConsequenceText = consequence
        };
    }

    /// <summary>Creates a separator item</summary>
    public static DropdownAction CreateSeparator()
    {
        return new DropdownAction { IsSeparator = true };
    }

    /// <summary>Creates a success/positive action</summary>
    public static DropdownAction CreateSuccess(string title, EventCallback onClick, string description = "")
    {
        return new DropdownAction
        {
            Title = title,
            Description = description,
            IconClass = "fas fa-check",
            Type = ActionType.Success,
            OnClick = onClick
        };
    }
}