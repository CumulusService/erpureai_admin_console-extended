// Theme Changer - Based on Telerik approach
var themeChanger = {
    
    // Initialize theme system
    init: function () {
        console.log('Theme changer initialized');
        
        // Load saved theme or detect system preference
        var savedTheme = localStorage.getItem('admin-theme');
        if (savedTheme) {
            this.applyTheme(savedTheme === 'dark');
        } else {
            // Detect system preference
            var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
            this.applyTheme(prefersDark);
        }
        
        // Listen for system theme changes
        if (window.matchMedia) {
            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
                // Only auto-switch if no manual preference is saved
                if (!localStorage.getItem('admin-theme')) {
                    this.applyTheme(e.matches);
                }
            });
        }
    },
    
    // Toggle between light and dark themes
    toggleTheme: function () {
        try {
            console.log('Toggle theme called');
            var linkElement = document.getElementById('AppThemeLink');
            if (!linkElement) {
                console.error('AppThemeLink element not found');
                return;
            }
            
            var currentTheme = linkElement.getAttribute('href');
            console.log('Current theme:', currentTheme);
            
            var isDark = currentTheme.includes('dark-theme');
            var newIsDark = !isDark;
            
            console.log('Switching to:', newIsDark ? 'dark' : 'light');
            
            this.applyTheme(newIsDark);
            
            // Save preference
            localStorage.setItem('admin-theme', newIsDark ? 'dark' : 'light');
            
            console.log('Theme toggled successfully to:', newIsDark ? 'dark' : 'light');
        } catch (error) {
            console.error('Error in toggleTheme:', error);
        }
    },
    
    // Apply theme by swapping CSS files
    applyTheme: function (isDark) {
        try {
            console.log('Apply theme called with isDark:', isDark);
            
            var lightTheme = "css/light-theme.css";
            var darkTheme = "css/dark-theme.css";
            
            var oldLink = document.getElementById('AppThemeLink');
            if (!oldLink) {
                console.error('AppThemeLink element not found for theme switching');
                return;
            }
            
            var newTheme = isDark ? darkTheme : lightTheme;
            console.log('New theme path:', newTheme);
            
            // Don't change if already correct theme
            var currentHref = oldLink.getAttribute('href');
            console.log('Current href:', currentHref);
            
            if (currentHref === newTheme || currentHref === ('~/' + newTheme)) {
                console.log('Theme already applied, skipping');
                return;
            }
            
            // Create new link element
            var newLink = document.createElement('link');
            newLink.setAttribute('id', 'AppThemeLink');
            newLink.setAttribute('rel', 'stylesheet');
            newLink.setAttribute('type', 'text/css');
            newLink.setAttribute('href', newTheme);
            
            console.log('Created new link element with href:', newTheme);
            
            // Replace old link with new one
            newLink.onload = function() {
                console.log('New theme CSS loaded successfully');
                if (oldLink && oldLink.parentElement) {
                    oldLink.parentElement.removeChild(oldLink);
                    console.log('Old theme CSS removed');
                }
            };
            
            newLink.onerror = function() {
                console.error('Failed to load theme CSS:', newTheme);
            };
            
            document.getElementsByTagName('head')[0].appendChild(newLink);
            
            console.log('Applied theme:', isDark ? 'dark' : 'light');
        } catch (error) {
            console.error('Error in applyTheme:', error);
        }
    }
};

// Make available globally
window.themeChanger = themeChanger;