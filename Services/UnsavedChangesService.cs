namespace AdminConsole.Services;

/// <summary>
/// Implementation of unsaved changes tracking service
/// </summary>
public class UnsavedChangesService : IUnsavedChangesService
{
    private readonly HashSet<string> _formsWithUnsavedChanges = new();
    private readonly object _lock = new();

    public event EventHandler<UnsavedChangesEventArgs>? UnsavedChangesChanged;

    public void MarkFormAsChanged(string formId)
    {
        if (string.IsNullOrWhiteSpace(formId)) return;

        bool wasAdded;
        lock (_lock)
        {
            wasAdded = _formsWithUnsavedChanges.Add(formId);
        }

        if (wasAdded)
        {
            UnsavedChangesChanged?.Invoke(this, new UnsavedChangesEventArgs 
            { 
                FormId = formId, 
                HasUnsavedChanges = true 
            });
        }
    }

    public void MarkFormAsSaved(string formId)
    {
        if (string.IsNullOrWhiteSpace(formId)) return;

        bool wasRemoved;
        lock (_lock)
        {
            wasRemoved = _formsWithUnsavedChanges.Remove(formId);
        }

        if (wasRemoved)
        {
            UnsavedChangesChanged?.Invoke(this, new UnsavedChangesEventArgs 
            { 
                FormId = formId, 
                HasUnsavedChanges = false 
            });
        }
    }

    public bool HasUnsavedChanges()
    {
        lock (_lock)
        {
            return _formsWithUnsavedChanges.Count > 0;
        }
    }

    public bool HasUnsavedChanges(string formId)
    {
        if (string.IsNullOrWhiteSpace(formId)) return false;

        lock (_lock)
        {
            return _formsWithUnsavedChanges.Contains(formId);
        }
    }

    public List<string> GetFormsWithUnsavedChanges()
    {
        lock (_lock)
        {
            return _formsWithUnsavedChanges.ToList();
        }
    }

    public void ClearAll()
    {
        List<string> clearedForms;
        lock (_lock)
        {
            clearedForms = _formsWithUnsavedChanges.ToList();
            _formsWithUnsavedChanges.Clear();
        }

        // Notify for each cleared form
        foreach (var formId in clearedForms)
        {
            UnsavedChangesChanged?.Invoke(this, new UnsavedChangesEventArgs 
            { 
                FormId = formId, 
                HasUnsavedChanges = false 
            });
        }
    }
}