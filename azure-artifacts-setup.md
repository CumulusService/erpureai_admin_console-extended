# Azure Artifacts Setup for SAP HANA Client Package

## Step 1: Create Azure DevOps Organization (if you don't have one)

1. Go to [Azure DevOps](https://dev.azure.com)
2. Sign in with your Azure account
3. Create a new organization or use existing one
4. Create a new project called "AdminConsole-Packages" or use existing project

## Step 2: Create Azure Artifacts Feed

1. In your Azure DevOps project, go to **Artifacts** in the left sidebar
2. Click **"+ Create Feed"**
3. Configure the feed:
   - **Name**: `sap-hana-packages`
   - **Visibility**: `People in my organization`
   - **Upstream sources**: Check "Include packages from common public sources"
   - **Scope**: `Project` or `Organization` (recommend Organization for enterprise use)

4. Click **"Create"**

## Step 3: Get Feed URL and Authentication

After creating the feed, you'll see:
- **Feed URL**: `https://pkgs.dev.azure.com/{organization}/_packaging/sap-hana-packages/nuget/v3/index.json`
- **Connect to Feed**: Click this to see authentication options

## Step 4: Create Personal Access Token (PAT)

1. Click your profile picture → **Personal access tokens**
2. Click **"+ New Token"**
3. Configure:
   - **Name**: `AdminConsole-NuGet-Push`
   - **Organization**: Your organization
   - **Expiration**: 1 year (or as per your policy)
   - **Scopes**: Select "Packaging (read, write, & manage)"
4. Click **"Create"**
5. **IMPORTANT**: Copy the token immediately (you won't see it again)

## Step 5: Upload SAP HANA Client Package

Using the generated NuGet package, upload it to Azure Artifacts:

### Option A: Using Azure DevOps Web Interface
1. In your feed, click **"Connect to feed"**
2. Select **"NuGet.exe"**
3. Follow the upload instructions

### Option B: Using Command Line (Preferred)
```bash
# Install Azure Artifacts Credential Provider (one-time setup)
iex "& { $(irm https://aka.ms/install-artifacts-credprovider.ps1) }"

# Or download and install from: https://github.com/microsoft/artifacts-credprovider

# Add your Azure Artifacts feed as a NuGet source
dotnet nuget add source "https://pkgs.dev.azure.com/{organization}/_packaging/sap-hana-packages/nuget/v3/index.json" --name "AzureArtifacts"

# Push the package (you'll be prompted for credentials)
dotnet nuget push "LocalNuGetPackages/SAPHANAClient.1.0.0.nupkg" --source "AzureArtifacts"
```

## Step 6: GitHub Actions Configuration

The following secrets need to be added to your GitHub repository:

1. **AZURE_ARTIFACTS_FEED_URL**: `https://pkgs.dev.azure.com/{organization}/_packaging/sap-hana-packages/nuget/v3/index.json`
2. **AZURE_ARTIFACTS_TOKEN**: The PAT token you created

## Next Steps

After completing these steps:
1. Update nuget.config to point to Azure Artifacts
2. Update GitHub Actions workflow to authenticate with Azure Artifacts
3. Test the deployment pipeline

## Cost Information

Azure Artifacts pricing (as of 2025):
- **Free tier**: 2 GiB storage + bandwidth included
- **Additional storage**: $2 per GiB/month
- **Additional bandwidth**: $2 per GiB

For a single SAP HANA client package (~50-100MB), you'll stay well within the free tier.