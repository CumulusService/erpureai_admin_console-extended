// Unsaved Changes Detection JavaScript Module
window.unsavedChanges = {
    hasUnsavedChanges: false,
    customMessage: "You have unsaved changes. Are you sure you want to leave without saving?",
    beforeUnloadHandler: null,
    
    // Enable unsaved changes detection
    enable: function(message) {
        console.log('Enabling unsaved changes detection with message:', message);
        this.hasUnsavedChanges = true;
        if (message) {
            this.customMessage = message;
        }
        this.attachEventHandlers();
    },
    
    // Disable unsaved changes detection
    disable: function() {
        console.log('Disabling unsaved changes detection');
        this.hasUnsavedChanges = false;
        this.detachEventHandlers();
    },
    
    // Set custom warning message
    setMessage: function(message) {
        this.customMessage = message;
    },
    
    // Attach event handlers for page navigation
    attachEventHandlers: function() {
        console.log('Attaching event handlers');
        
        // Remove existing handler if any
        if (this.beforeUnloadHandler) {
            window.removeEventListener('beforeunload', this.beforeUnloadHandler);
        }
        
        // Create and attach new handler
        this.beforeUnloadHandler = (event) => {
            console.log('beforeunload event fired, hasUnsavedChanges:', this.hasUnsavedChanges);
            if (this.hasUnsavedChanges) {
                // Modern browsers require this exact pattern
                event.preventDefault();
                event.returnValue = '';
                return '';
            }
        };
        
        window.addEventListener('beforeunload', this.beforeUnloadHandler);
        
        // Also try to intercept link clicks
        this.interceptLinkClicks();
    },
    
    // Detach event handlers
    detachEventHandlers: function() {
        console.log('Detaching event handlers');
        if (this.beforeUnloadHandler) {
            window.removeEventListener('beforeunload', this.beforeUnloadHandler);
            this.beforeUnloadHandler = null;
        }
    },
    
    // Intercept link clicks for internal navigation
    interceptLinkClicks: function() {
        // This is a simple approach - intercept all link clicks
        document.addEventListener('click', (event) => {
            if (this.hasUnsavedChanges && event.target && event.target.tagName === 'A') {
                const confirmed = confirm(this.customMessage);
                if (!confirmed) {
                    event.preventDefault();
                    event.stopPropagation();
                    return false;
                }
            }
        }, true);
    },
    
    // Manually trigger unsaved changes warning (for programmatic navigation)
    confirmNavigation: function(message) {
        console.log('confirmNavigation called, hasUnsavedChanges:', this.hasUnsavedChanges);
        if (this.hasUnsavedChanges) {
            const confirmMessage = message || this.customMessage;
            return confirm(confirmMessage);
        }
        return true;
    },
    
    // Force disable (for successful form submissions)
    forceDisable: function() {
        console.log('Force disabling unsaved changes');
        this.hasUnsavedChanges = false;
        this.detachEventHandlers();
    }
};

// Initialize on page load
document.addEventListener('DOMContentLoaded', function() {
    console.log('Unsaved changes detection JavaScript loaded');
});

// Debug logging
window.unsavedChanges.debug = function() {
    console.log('Unsaved changes state:', {
        hasUnsavedChanges: this.hasUnsavedChanges,
        customMessage: this.customMessage
    });
};