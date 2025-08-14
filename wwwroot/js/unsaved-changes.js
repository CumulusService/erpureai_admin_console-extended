// Enhanced Unsaved Changes Detection JavaScript Module
window.unsavedChanges = {
    hasUnsavedChanges: false,
    customMessage: "You have unsaved changes. Are you sure you want to leave without saving?",
    beforeUnloadHandler: null,
    clickHandler: null,
    modalTimeouts: new Set(),
    isDialogOpen: false,
    
    // Enable unsaved changes detection - DISABLED to remove browser notifications
    enable: function(message) {
        console.log('Unsaved changes detection DISABLED - no browser notifications');
        // Intentionally do nothing - browser notifications completely removed
        return;
    },
    
    // Disable unsaved changes detection
    disable: function() {
        console.log('Disabling unsaved changes detection');
        this.hasUnsavedChanges = false;
        this.detachEventHandlers();
        this.clearAllTimeouts();
    },
    
    // Set custom warning message - DISABLED
    setMessage: function(message) {
        // Intentionally do nothing - browser notifications completely removed
        return;
    },
    
    // Clear all timeouts
    clearAllTimeouts: function() {
        this.modalTimeouts.forEach(timeout => clearTimeout(timeout));
        this.modalTimeouts.clear();
    },
    
    // Enhanced modal confirmation with fallback mechanisms
    showConfirmationDialog: function(message, callback) {
        if (this.isDialogOpen) {
            console.log('Dialog already open, using existing result');
            return false;
        }

        this.isDialogOpen = true;
        const confirmMessage = message || this.customMessage;
        
        try {
            // Primary confirmation method
            const result = this.showBootstrapModal(confirmMessage, callback);
            if (result !== null) {
                return result;
            }
            
            // Fallback to native confirm
            return this.showNativeConfirm(confirmMessage, callback);
        } catch (error) {
            console.error('Error showing confirmation dialog:', error);
            // Ultimate fallback - assume user wants to proceed
            this.isDialogOpen = false;
            return true;
        }
    },
    
    // Bootstrap modal confirmation (primary method)
    showBootstrapModal: function(message, callback) {
        try {
            // Check if Bootstrap is available
            if (typeof bootstrap === 'undefined') {
                return null;
            }
            
            // Create modal HTML
            const modalId = 'unsavedChangesModal_' + Date.now();
            const modalHtml = `
                <div class="modal fade" id="${modalId}" tabindex="-1" aria-hidden="true" data-bs-backdrop="static" data-bs-keyboard="false">
                    <div class="modal-dialog modal-dialog-centered">
                        <div class="modal-content">
                            <div class="modal-header">
                                <h5 class="modal-title">
                                    <i class="fas fa-exclamation-triangle text-warning me-2"></i>
                                    Unsaved Changes
                                </h5>
                            </div>
                            <div class="modal-body">
                                <p class="mb-0">${message}</p>
                            </div>
                            <div class="modal-footer">
                                <button type="button" class="btn btn-secondary" data-action="stay">
                                    <i class="fas fa-times me-1"></i>
                                    Cancel
                                </button>
                                <button type="button" class="btn btn-danger" data-action="leave">
                                    <i class="fas fa-sign-out-alt me-1"></i>
                                    Leave Without Saving
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            `;
            
            // Add modal to page
            const modalContainer = document.createElement('div');
            modalContainer.innerHTML = modalHtml;
            document.body.appendChild(modalContainer);
            
            const modalElement = document.getElementById(modalId);
            const modal = new bootstrap.Modal(modalElement);
            
            let resolved = false;
            
            // Handle button clicks
            modalElement.addEventListener('click', (e) => {
                const action = e.target.getAttribute('data-action');
                if (action && !resolved) {
                    resolved = true;
                    const shouldLeave = action === 'leave';
                    
                    modal.hide();
                    
                    // Clean up after modal is hidden
                    const hideTimeout = setTimeout(() => {
                        this.isDialogOpen = false;
                        modalContainer.remove();
                        this.modalTimeouts.delete(hideTimeout);
                        if (callback) callback(shouldLeave);
                    }, 300);
                    
                    this.modalTimeouts.add(hideTimeout);
                    return shouldLeave;
                }
            });
            
            // Auto-cleanup timeout
            const cleanupTimeout = setTimeout(() => {
                if (!resolved) {
                    resolved = true;
                    this.isDialogOpen = false;
                    modal.hide();
                    modalContainer.remove();
                    console.log('Modal auto-cleanup - assuming user wants to stay');
                    if (callback) callback(false);
                }
                this.modalTimeouts.delete(cleanupTimeout);
            }, 30000); // 30 second timeout
            
            this.modalTimeouts.add(cleanupTimeout);
            
            // Show modal
            modal.show();
            
            return null; // Async handling via callback
        } catch (error) {
            console.error('Error showing Bootstrap modal:', error);
            return null;
        }
    },
    
    // Native confirm fallback
    showNativeConfirm: function(message, callback) {
        try {
            const result = confirm(message);
            this.isDialogOpen = false;
            
            // Add delay to ensure responsiveness
            const timeout = setTimeout(() => {
                if (callback) callback(result);
                this.modalTimeouts.delete(timeout);
            }, 10);
            
            this.modalTimeouts.add(timeout);
            return result;
        } catch (error) {
            console.error('Error showing native confirm:', error);
            this.isDialogOpen = false;
            if (callback) callback(true);
            return true;
        }
    },
    
    // Attach event handlers - COMPLETELY DISABLED
    attachEventHandlers: function() {
        console.log('Event handlers DISABLED - no browser notifications will be attached');
        // Intentionally do nothing - all browser notifications completely removed
        return;
        
        // Create enhanced click handler
        this.clickHandler = (event) => {
            if (this.hasUnsavedChanges && !this.isDialogOpen) {
                let target = event.target;
                
                // Walk up the DOM to find clickable elements
                while (target && target !== document) {
                    if (this.shouldInterceptElement(target)) {
                        event.preventDefault();
                        event.stopPropagation();
                        event.stopImmediatePropagation();
                        
                        const href = target.href || target.getAttribute('data-href');
                        this.showConfirmationDialog(this.customMessage, (shouldLeave) => {
                            if (shouldLeave) {
                                this.hasUnsavedChanges = false;
                                if (href) {
                                    window.location.href = href;
                                } else if (target.click) {
                                    target.click();
                                }
                            }
                        });
                        return false;
                    }
                    target = target.parentElement;
                }
            }
        };
        
        document.addEventListener('click', this.clickHandler, true);
    },
    
    // Determine if element should be intercepted
    shouldInterceptElement: function(element) {
        if (!element) return false;
        
        const tagName = element.tagName?.toLowerCase();
        const role = element.getAttribute('role');
        const href = element.href || element.getAttribute('data-href');
        const onclick = element.onclick || element.getAttribute('onclick');
        
        // Links
        if (tagName === 'a' && href) return true;
        
        // Buttons with navigation
        if (tagName === 'button' && (href || onclick)) return true;
        
        // Elements with navigation role
        if (role && (role.includes('button') || role.includes('link')) && (href || onclick)) return true;
        
        // Bootstrap nav items
        if (element.classList?.contains('nav-link') || element.classList?.contains('btn')) return true;
        
        return false;
    },
    
    // Detach event handlers
    detachEventHandlers: function() {
        console.log('Detaching event handlers (browser notifications disabled)');
        // beforeUnloadHandler removed - no browser notifications
        if (this.clickHandler) {
            document.removeEventListener('click', this.clickHandler, true);
            this.clickHandler = null;
        }
    },
    
    // Manually trigger unsaved changes warning (for programmatic navigation)
    confirmNavigation: function(message) {
        console.log('confirmNavigation called, hasUnsavedChanges:', this.hasUnsavedChanges);
        if (this.hasUnsavedChanges && !this.isDialogOpen) {
            const confirmMessage = message || this.customMessage;
            
            try {
                // For synchronous calls, use native confirm
                return confirm(confirmMessage);
            } catch (error) {
                console.error('Error in confirmNavigation:', error);
                return true;
            }
        }
        return true;
    },
    
    // Async version of confirmNavigation
    confirmNavigationAsync: function(message, callback) {
        console.log('confirmNavigationAsync called, hasUnsavedChanges:', this.hasUnsavedChanges);
        if (this.hasUnsavedChanges && !this.isDialogOpen) {
            const confirmMessage = message || this.customMessage;
            this.showConfirmationDialog(confirmMessage, callback);
        } else {
            if (callback) callback(true);
        }
    },
    
    // Force disable (for successful form submissions)
    forceDisable: function() {
        console.log('Force disabling unsaved changes');
        this.hasUnsavedChanges = false;
        this.isDialogOpen = false;
        this.detachEventHandlers();
        this.clearAllTimeouts();
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