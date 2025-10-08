# 📝 Placeholder Replacement Guide

**Before publishing, replace these placeholders with your actual information!**

## 🔍 **Find & Replace List**

Use your editor's "Find & Replace" feature (Ctrl+H) to replace these placeholders:

### **Your Identity**

| Placeholder | Description | Example | Files to Update |
|-------------|-------------|---------|-----------------|
| `[YOUR NAME/COMPANY]` | Your name or company name | `John Doe` or `Acme Corp` | LICENSE, COMMERCIAL_LICENSE.md, README.md, CHANGELOG.md |
| `[YOUR NAME]` | Your full name | `John Doe` | Several docs |

### **Contact Information**

| Placeholder | Description | Example | Files to Update |
|-------------|-------------|---------|-----------------|
| `[YOUR_EMAIL@example.com]` | Your business email | `john@sportrental.com` | README.md, LICENSE, COMMERCIAL_LICENSE.md, CODE_OF_CONDUCT.md |
| `[YOUR_PHONE_NUMBER]` | Your phone number | `+1 (555) 123-4567` | COMMERCIAL_LICENSE.md |
| `[YOUR_WEBSITE.com]` | Your website URL | `https://sportrental.com` | README.md, COMMERCIAL_LICENSE.md, LICENSE |
| `[YOUR_LINKEDIN]` | Your LinkedIn profile | `john-doe-12345` | README.md, COMMERCIAL_LICENSE.md |
| `[YOUR_LINKEDIN_PROFILE]` | Full LinkedIn URL | `https://linkedin.com/in/john-doe` | COMMERCIAL_LICENSE.md |

### **Repository Information**

| Placeholder | Description | Example | Files to Update |
|-------------|-------------|---------|-----------------|
| `YOUR_USERNAME` | Your GitHub username | `johndoe` | README.md, CHANGELOG.md, CONTRIBUTING.md, docs/QUICKSTART.md |
| `ORIGINAL_OWNER` | Original repository owner | `johndoe` | CONTRIBUTING.md |

### **Legal Information**

| Placeholder | Description | Example | Files to Update |
|-------------|-------------|---------|-----------------|
| `[YOUR_JURISDICTION]` | Legal jurisdiction | `Poland` or `United States` | LICENSE |
| `[INSERT CONTACT EMAIL]` | Contact for code of conduct | `conduct@sportrental.com` | CODE_OF_CONDUCT.md |

---

## 📋 **Step-by-Step Replacement**

### **1. Open VS Code (or your editor)**

```bash
code .
```

### **2. Use Find & Replace (Ctrl+Shift+H)**

For each placeholder above:

1. **Find:** `[YOUR_EMAIL@example.com]`
2. **Replace with:** `your-actual-email@domain.com`
3. **Replace All**

### **3. Manual Review**

Some placeholders require context-specific replacement:

#### **README.md**
- Line ~40: Replace `YOUR_USERNAME` in clone URL
- Line ~200: Replace all contact info in Contact section

#### **COMMERCIAL_LICENSE.md**
- Update pricing if needed (currently in EUR)
- Update all contact information
- Customize license tiers if needed

#### **LICENSE**
- Replace `[YOUR NAME/COMPANY]` in copyright
- Replace `[YOUR_EMAIL@example.com]` in contact section
- Replace `[YOUR_JURISDICTION]` (e.g., "Poland", "California, USA")

#### **CHANGELOG.md**
- Update repository URLs at the bottom

#### **docs/QUICKSTART.md**
- Update clone URL
- Update links to issues/discussions

---

## ✅ **Verification Checklist**

After replacement, verify:

- [ ] **All placeholder brackets removed** - Search for `[YOUR` to find any missed
- [ ] **Email addresses are valid** - No typos
- [ ] **URLs work** - Test GitHub, LinkedIn, website links
- [ ] **Phone number formatted correctly**
- [ ] **Company name consistent** - Same everywhere
- [ ] **Copyright year correct** - Update to 2024 or 2025

---

## 🔍 **Quick Search Commands**

### **Find remaining placeholders:**

```bash
# Search for remaining placeholders
grep -r "\[YOUR" .

# Specific files only
grep "\[YOUR" README.md LICENSE COMMERCIAL_LICENSE.md
```

### **PowerShell (Windows):**

```powershell
# Find all placeholders
Select-String -Path "*.md", "LICENSE" -Pattern "\[YOUR" -Recurse

# List files with placeholders
Get-ChildItem -Recurse -Include *.md, LICENSE | Select-String "\[YOUR" | Select-Object Path -Unique
```

---

## 📝 **Example Replacements**

### **Before:**
```markdown
Contact: [YOUR_EMAIL@example.com]
Website: [YOUR_WEBSITE.com]
Copyright (c) 2024 [YOUR NAME/COMPANY]
```

### **After:**
```markdown
Contact: john@sportrental.com
Website: https://sportrental.com
Copyright (c) 2024 John Doe Software Solutions
```

---

## 🎯 **Priority Files**

**Replace placeholders in this order:**

1. **LICENSE** (most important legally!)
2. **README.md** (first thing visitors see)
3. **COMMERCIAL_LICENSE.md** (for potential customers)
4. **CONTRIBUTING.md**
5. **CODE_OF_CONDUCT.md**
6. **CHANGELOG.md**
7. **docs/QUICKSTART.md**

---

## ⚠️ **Important Notes**

### **Email Addresses**
- Use a **business email** (not personal Gmail/Hotmail)
- Consider creating: `licensing@yourdomain.com`, `support@yourdomain.com`

### **Legal Jurisdiction**
- Consult a lawyer for the correct jurisdiction
- Common options: Your country, your state/province, or EU

### **Pricing**
- Review pricing in COMMERCIAL_LICENSE.md
- Adjust based on your market and costs

### **Copyright**
- Use your actual legal name or registered company name
- Update year to current year

---

## 🚀 **After Replacement**

1. **Commit changes:**
   ```bash
   git add .
   git commit -m "docs: replace placeholders with actual information"
   ```

2. **Do final review:**
   ```bash
   # Check for missed placeholders
   grep -r "YOUR\|PLACEHOLDER\|TODO\|FIXME" . --include="*.md"
   ```

3. **Push to GitHub:**
   ```bash
   git push
   ```

---

## 💡 **Optional Customizations**

Consider customizing:
- **Pricing tiers** in COMMERCIAL_LICENSE.md
- **License types** (add more tiers if needed)
- **Contact methods** (add Discord, Slack, etc.)
- **Badges** in README.md (add build status, coverage, etc.)

---

**Good luck with your launch! 🚀**
