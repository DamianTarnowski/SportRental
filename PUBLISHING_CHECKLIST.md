# 📋 Publishing Checklist - SportRental

**Before pushing to GitHub, complete this checklist!**

## 🔒 Security Verification

- [ ] **No secrets in code**
  ```bash
  # Search for potential secrets
  git grep -i "password"
  git grep -i "connectionstring"
  git grep -i "sk_test_"
  git grep -i "pk_test_"
  git grep -i "AccountKey"
  ```

- [ ] **Check .gitignore is working**
  ```bash
  git status
  # Should NOT show:
  # - appsettings.Development.json
  # - appsettings.Test.json
  # - Sport Rental old project/
  # - stripe.exe
  # - test-data.json
  # - *.db files
  ```

- [ ] **Verify appsettings.json are clean**
  ```bash
  cat SportRental.Api/appsettings.json
  cat SportRental.Admin/appsettings.json
  # Should only have KeyVault:Url (empty) and logging config
  ```

- [ ] **Remove personal identifiers**
  - Email addresses
  - Phone numbers
  - Personal Azure subscription IDs
  - Real database credentials

## 📝 Documentation

- [ ] **README.md is complete**
  - Project description ✅
  - Architecture diagram ✅
  - Setup instructions ✅
  - Feature list ✅
  - Technology stack ✅

- [ ] **SECURITY.md exists** ✅
  - Azure Key Vault instructions
  - Responsible disclosure policy

- [ ] **CONTRIBUTING.md exists** ✅
  - How to contribute
  - Code standards
  - PR process

- [ ] **LICENSE file exists** ✅ (MIT)

- [ ] **CHANGELOG.md started** ✅

## 🧪 Testing

- [ ] **All tests pass**
  ```bash
  dotnet test
  # Should: 356/360 tests pass (4 skipped manual tests)
  ```

- [ ] **No broken references**
  ```bash
  dotnet build
  # Should build without errors
  ```

## 📦 Repository Structure

- [ ] **No build artifacts**
  - bin/ folders (in .gitignore ✅)
  - obj/ folders (in .gitignore ✅)
  - node_modules/ (in .gitignore ✅)

- [ ] **No large files**
  ```bash
  find . -type f -size +10M
  # Should not show any files (or only necessary ones)
  ```

- [ ] **Old project excluded**
  - "Sport Rental old project/" is in .gitignore ✅

## 🎨 Professional Touches

- [ ] **GitHub templates created**
  - Bug report template ✅
  - Feature request template ✅
  - PR template ✅

- [ ] **GitHub Actions configured** ✅
  - CI/CD pipeline
  - Security scanning
  - Code coverage

- [ ] **Badges in README** ✅
  - .NET version
  - Test count
  - Status

## 🚀 Before First Commit

```bash
# 1. Review what will be committed
git status

# 2. Add files (careful!)
git add .

# 3. Review staged changes
git diff --cached

# 4. Check for accidentally staged secrets
git diff --cached | grep -i "password\|secret\|key"

# 5. Commit
git commit -m "feat: initial commit - SportRental multi-tenant platform"

# 6. Create GitHub repo (don't push yet!)
# Go to github.com and create new repository

# 7. Add remote
git remote add origin https://github.com/DamianTarnowski/SportRental.git

# 8. Final check before push
git log --oneline
git show HEAD

# 9. Push to GitHub
git push -u origin master
```

## 🔧 Post-Publication Setup

### Update URLs in files:

1. **README.md**
   - Replace `YOUR_USERNAME` with your GitHub username
   - Update clone URL

2. **CHANGELOG.md**
   - Update repository links

3. **CONTRIBUTING.md**
   - Update repository URLs

4. **docs/QUICKSTART.md**
   - Update clone URL
   - Update issue/discussion links

5. **.github/FUNDING.yml**
   - Add your funding links (optional)

### GitHub Repository Settings:

- [ ] **Description**: "Multi-tenant sport equipment rental platform with .NET 9, Blazor, Stripe payments, and Azure integration"

- [ ] **Topics** (tags):
  - `dotnet`
  - `blazor`
  - `csharp`
  - `asp-net-core`
  - `multi-tenant`
  - `stripe`
  - `azure`
  - `postgresql`
  - `rental-management`
  - `saas`

- [ ] **Enable GitHub Pages** (for documentation)
  - Settings → Pages → Source: `main` branch, `/docs` folder

- [ ] **Branch Protection Rules** (recommended):
  - Require PR reviews before merging
  - Require status checks to pass
  - Require branches to be up to date

- [ ] **Security Settings**:
  - Enable Dependabot alerts
  - Enable code scanning (CodeQL)
  - Enable secret scanning

## ✅ Final Verification

```bash
# Clone your repo in a fresh directory to test
cd /tmp
git clone https://github.com/YOUR_USERNAME/SportRental.git
cd SportRental

# Try to build
dotnet build

# Check for any leftover secrets
grep -r "sk_test_" .
grep -r "DefaultEndpointsProtocol" .
grep -r "Password=" . --include="*.cs" --include="*.json"

# Should find nothing!
```

## 🎉 Congratulations!

Your repository is now **professionally prepared** and ready for:
- ⭐ GitHub stars
- 🤝 Contributions
- 📢 Sharing
- 💼 Portfolio showcase

---

**Remember**: If you ever find a secret that was accidentally committed:
1. **DO NOT** just remove it in a new commit
2. Use `git filter-branch` or BFG Repo-Cleaner to rewrite history
3. Rotate all exposed credentials immediately
4. See [SECURITY.md](SECURITY.md) for more info
