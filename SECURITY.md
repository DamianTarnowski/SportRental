# 🔐 Security Guide

## ⚠️ **IMPORTANT: This project uses Azure Key Vault for secrets management**

**NO SECRETS ARE STORED IN CODE OR CONFIGURATION FILES!**

All sensitive data (passwords, API keys, connection strings) are stored securely in Azure Key Vault.

---

## 🚀 **Quick Start for Development**

### **1. Prerequisites**
- Azure CLI installed: https://aka.ms/installazurecliwindows
- Azure account with access to Key Vault
- .NET 10 SDK

### **2. Login to Azure**
```bash
az login --tenant YOUR-TENANT-ID
```

### **3. Configure appsettings.Development.json**
Copy `appsettings.Development.json.example` to `appsettings.Development.json` in both projects:
- `SportRental.Admin/appsettings.Development.json`
- `SportRental.Api/appsettings.Development.json`

Set your Key Vault URL:
```json
{
  "KeyVault": {
    "Url": "https://your-keyvault-name.vault.azure.net/"
  }
}
```

### **4. Request Access to Key Vault**
Contact the project owner to grant you access:
- Role: `Key Vault Secrets User` (read-only)
- Or: `Key Vault Secrets Officer` (read/write)

### **5. Run the application**
```bash
dotnet run --project SportRental.Admin
# or
dotnet run --project SportRental.Api
```

The application will automatically:
- Detect your Azure CLI login
- Connect to Key Vault
- Load all secrets

---

## 🔑 **What's in Key Vault?**

All secrets follow the naming convention: `Section--Subsection--Key`

Example secrets:
- `ConnectionStrings--DefaultConnection` - PostgreSQL database
- `Stripe--SecretKey` - Stripe payment gateway
- `Jwt--SigningKey` - JWT token signing
- `Email--Smtp--Password` - Email service
- `Storage--AzureBlob--ConnectionString` - Azure Blob Storage

---

## 🏭 **Production Deployment (Azure)**

In production, the application uses **Managed Identity** instead of Azure CLI:

1. Enable System-Assigned Managed Identity on your Azure App Service
2. Grant the Managed Identity access to Key Vault:
   ```bash
   az role assignment create \
     --role "Key Vault Secrets User" \
     --assignee MANAGED_IDENTITY_OBJECT_ID \
     --scope /subscriptions/SUB_ID/resourceGroups/RG_NAME/providers/Microsoft.KeyVault/vaults/VAULT_NAME
   ```
3. Set `KeyVault:Url` in Azure App Service Configuration
4. Deploy!

**No code changes required!** The same `DefaultAzureCredential` works everywhere.

---

## 📋 **Files Ignored by Git**

These files contain secrets and are **NEVER committed**:
- `**/appsettings.Development.json` - Local development settings
- `**/appsettings.Test.json` - Test configuration
- `test-data.json` - Test data with passwords
- `Sport Rental old project/` - Legacy code with hardcoded secrets
- `stripe.exe` - Stripe CLI binary

---

## ⚠️ **Security Best Practices**

### **DO:**
- ✅ Use Azure Key Vault for all secrets
- ✅ Use `DefaultAzureCredential` for authentication
- ✅ Rotate secrets regularly in Key Vault
- ✅ Use Managed Identity in production
- ✅ Review `.gitignore` before committing

### **DON'T:**
- ❌ Never commit secrets to Git
- ❌ Never hardcode passwords in code
- ❌ Never share your Key Vault URL publicly (it's OK in private repos)
- ❌ Never commit `appsettings.Development.json`
- ❌ Never disable `.gitignore` rules for secrets

---

## 🐛 **Troubleshooting**

### **"DefaultAzureCredential failed to retrieve a token"**
1. Make sure you're logged in: `az login --tenant YOUR-TENANT-ID`
2. Check you have access to Key Vault:
   ```bash
   az keyvault secret list --vault-name YOUR-VAULT-NAME
   ```
3. Verify the Key Vault URL in `appsettings.Development.json`

### **"Unable to find user with upn"**
Use RBAC roles instead of Access Policies. The Key Vault must have `--enable-rbac-authorization` enabled.

### **"Azure CLI not installed"**
Install Azure CLI: https://aka.ms/installazurecliwindows

---

## 📚 **More Information**

- [Azure Key Vault Setup Guide](doc/setup/AZURE_KEY_VAULT_SETUP.md)
- [DefaultAzureCredential Documentation](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential)
- [Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)

---

## 🆘 **Need Help?**

Contact the project maintainer for:
- Key Vault access
- Secret values
- Production deployment support

