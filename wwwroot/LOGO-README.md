# Company Logo Setup

## How to use your company logo

To use your company's logo in the ERPURE.AI Admin Console:

### Option 1: Replace the default logo files
1. Replace `company-logo.png` with your company's PNG logo (recommended size: 32px height, any width)
2. Optionally, replace `company-logo.ico` with your company's ICO file for browser favicon

### Option 2: Add new logo files
1. Add your logo files to the `wwwroot` folder as:
   - `company-logo.png` (main logo for navigation bar)
   - `company-logo.ico` (optional favicon for browser tab)

### Logo Requirements
- **PNG Format**: Recommended for navigation bar display
- **Height**: 32px (width will auto-scale to maintain aspect ratio)
- **ICO Format**: Optional, for browser favicon support
- **File Names**: Must be exactly `company-logo.png` and `company-logo.ico`

### Logo Display
- The logo appears in the navigation bar next to "ERPURE.AI Admin Console"
- If no custom logo is provided, the app will gracefully hide the logo area
- The logo is responsive and will maintain its aspect ratio

### Troubleshooting
- Clear browser cache after replacing logo files
- Ensure file names are exactly as specified (case-sensitive)
- Logo should be optimized for web display (small file size)