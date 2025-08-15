// 🚀 Enhanced Navigation Optimization Helper
// Based on Telerik Blazor Enhanced Navigation best practices

window.enhancedNavigation = {
    // Navigation performance tracking
    navigationStartTime: 0,
    
    // Initialize enhanced navigation optimizations
    initialize: function() {
        console.log('🚀 Enhanced Navigation: Initializing optimizations...');
        
        this.setupNavigationInterception();
        this.setupPreloadingOnHover();
        this.setupNavigationMetrics();
        
        console.log('✅ Enhanced Navigation: Optimizations active');
    },
    
    // Intercept navigation for performance tracking
    setupNavigationInterception: function() {
        // Track navigation start times
        document.addEventListener('click', (event) => {
            const link = event.target.closest('a[href]');
            if (link && this.isInternalNavigation(link)) {
                this.navigationStartTime = performance.now();
                this.showNavigationFeedback(link);
            }
        });
        
        // Listen for Blazor navigation completion
        if (window.Blazor) {
            // This will be called after enhanced navigation completes
            const originalOnNavigate = window.Blazor.navigate || (() => {});
            window.Blazor.navigate = (...args) => {
                this.onNavigationComplete();
                return originalOnNavigate.apply(window.Blazor, args);
            };
        }
    },
    
    // Preload resources when hovering over navigation links
    setupPreloadingOnHover: function() {
        let preloadTimeout;
        
        document.addEventListener('mouseover', (event) => {
            const link = event.target.closest('a[href]');
            if (link && this.isInternalNavigation(link)) {
                // Clear any existing timeout
                clearTimeout(preloadTimeout);
                
                // Preload after 100ms hover (prevents accidental preloads)
                preloadTimeout = setTimeout(() => {
                    this.preloadNavigation(link.getAttribute('href'));
                }, 100);
            }
        });
        
        document.addEventListener('mouseout', (event) => {
            const link = event.target.closest('a[href]');
            if (link) {
                clearTimeout(preloadTimeout);
            }
        });
    },
    
    // Setup navigation performance metrics
    setupNavigationMetrics: function() {
        // Log navigation performance in development
        if (window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1') {
            let navigationCount = 0;
            
            setInterval(() => {
                if (navigationCount > 0) {
                    console.log(`📊 Enhanced Navigation: ${navigationCount} navigations in last 10s`);
                    navigationCount = 0;
                }
            }, 10000);
            
            this.onNavigationComplete = () => {
                navigationCount++;
                const duration = performance.now() - this.navigationStartTime;
                if (duration < 1000) { // Only log fast navigations
                    console.log(`⚡ Fast navigation completed in ${duration.toFixed(1)}ms`);
                }
            };
        }
    },
    
    // Check if link is internal navigation
    isInternalNavigation: function(link) {
        const href = link.getAttribute('href');
        if (!href) return false;
        
        // Skip external links, hash links, and special protocols
        if (href.startsWith('http') || href.startsWith('#') || href.includes('://')) {
            return false;
        }
        
        // Skip if explicitly disabled
        if (link.getAttribute('data-enhance-nav') === 'false') {
            return false;
        }
        
        return true;
    },
    
    // Preload navigation target
    preloadNavigation: function(href) {
        // Use native browser preloading for the target page
        const link = document.createElement('link');
        link.rel = 'prefetch';
        link.href = href;
        link.as = 'document';
        
        // Add to head if not already present
        const existingLink = document.querySelector(`link[rel="prefetch"][href="${href}"]`);
        if (!existingLink) {
            document.head.appendChild(link);
            console.log(`🔄 Preloading: ${href}`);
            
            // Remove after 30 seconds to prevent memory buildup
            setTimeout(() => {
                if (link.parentNode) {
                    document.head.removeChild(link);
                }
            }, 30000);
        }
    },
    
    // Show navigation feedback for better UX
    showNavigationFeedback: function(link) {
        // Add subtle loading indicator to clicked link
        const originalText = link.innerHTML;
        const hasIcon = link.querySelector('i, span[class*="fa"]');
        
        if (hasIcon) {
            // Add spinner to existing icon
            const icon = link.querySelector('i, span[class*="fa"]');
            const originalClass = icon.className;
            icon.className = 'fas fa-circle-notch fa-spin';
            
            // Restore after navigation or timeout
            setTimeout(() => {
                if (icon) icon.className = originalClass;
            }, 2000);
        }
    },
    
    // Called when navigation completes
    onNavigationComplete: function() {
        const duration = performance.now() - this.navigationStartTime;
        
        // Update performance metrics if available
        if (window.performanceMetrics) {
            window.performanceMetrics.recordNavigation(duration);
        }
    }
};

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.enhancedNavigation.initialize();
    });
} else {
    window.enhancedNavigation.initialize();
}