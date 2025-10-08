# Contributing to SportRental

🎉 **Thank you for your interest in SportRental!**

⚠️ **IMPORTANT:** SportRental is **proprietary software**. While the source code is publicly visible, it is NOT open source.

## Contribution Policy

Due to the proprietary nature of this software, we have specific guidelines for contributions:

- ✅ **Bug Reports** - We welcome bug reports and security vulnerability disclosures
- ✅ **Feature Suggestions** - Share your ideas for improvements
- ❌ **Code Contributions** - We do NOT accept pull requests from external contributors
- ❌ **Forks** - Forking for commercial use is prohibited without a license

**For commercial licensing inquiries, see [COMMERCIAL_LICENSE.md](COMMERCIAL_LICENSE.md)**

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Pull Request Process](#pull-request-process)
- [Coding Standards](#coding-standards)
- [Running Tests](#running-tests)
- [Commit Message Guidelines](#commit-message-guidelines)

## 📜 Code of Conduct

This project adheres to a Code of Conduct. By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

## 🚀 Getting Started

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/DamianTarnowski/SportRental.git
   cd SportRental
   ```
3. **Add upstream remote**:
   ```bash
   git remote add upstream https://github.com/DamianTarnowski/SportRental.git
   ```

## 🛠️ Development Setup

### Prerequisites

- **.NET 9 SDK** or later
- **PostgreSQL 14+** for main database
- **Azure CLI** (for Key Vault access)
- **Stripe CLI** (optional, for payment testing)
- **Node.js 18+** (for Blazor WASM client)

### Configuration

1. **Copy configuration template**:
   ```bash
   cp appsettings.Development.json.template SportRental.Api/appsettings.Development.json
   cp appsettings.Development.json.template SportRental.Admin/appsettings.Development.json
   ```

2. **Configure Azure Key Vault** (recommended):
   - Create an Azure Key Vault
   - Update `KeyVault:Url` in `appsettings.json`
   - Add secrets to Key Vault (see [SECURITY.md](SECURITY.md))
   - Login with Azure CLI: `az login`

3. **Or use local configuration** (development only):
   - Fill in the values in `appsettings.Development.json`
   - **⚠️ NEVER commit this file!**

4. **Setup database**:
   ```bash
   cd SportRental.Admin
   dotnet ef database update
   ```

5. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

## 🤝 How to Contribute

### Reporting Bugs

- **Check existing issues** first to avoid duplicates
- Use the **bug report template**
- Include:
  - Clear description of the bug
  - Steps to reproduce
  - Expected vs actual behavior
  - Environment details (.NET version, OS, etc.)
  - Screenshots/logs if applicable

### Suggesting Features

- **Check existing issues** for similar suggestions
- Use the **feature request template**
- Describe:
  - The problem you're trying to solve
  - Your proposed solution
  - Alternative solutions considered
  - Why this feature would benefit others

### Why We Don't Accept Pull Requests

As proprietary software, all code changes must be made by licensed developers to:
- ✅ Maintain code quality and consistency
- ✅ Ensure proper licensing and copyright
- ✅ Protect intellectual property
- ✅ Maintain support commitments to licensed customers

**However**, we value your input! Please share your ideas through:
- 💡 **Feature Requests** - Open an issue with your suggestion
- 🐛 **Bug Reports** - Help us improve quality
- 📧 **Direct Contact** - For significant contributions, contact us about licensing

## 🔄 Pull Request Process

1. **Update documentation** if needed (README, API docs, etc.)
2. **Add/update tests** for your changes
3. **Ensure all tests pass**: `dotnet test`
4. **Update CHANGELOG.md** (if applicable)
5. **Fill out the PR template** completely
6. **Link related issues** using keywords (closes #123, fixes #456)
7. **Wait for review** - maintainers will review your PR
8. **Address feedback** - make requested changes
9. **Merge** - once approved, your PR will be merged!

### PR Title Format

Follow [Conventional Commits](https://www.conventionalcommits.org/):

- `feat: add new feature`
- `fix: resolve bug in payment flow`
- `docs: update API documentation`
- `test: add tests for rental service`
- `refactor: simplify tenant middleware`
- `chore: update dependencies`

## 📐 Coding Standards

### C# / .NET

- Use **C# 12** features where appropriate
- Follow **Microsoft C# Coding Conventions**
- Use **nullable reference types**
- Prefer **async/await** for I/O operations
- Use **dependency injection** for services
- Write **XML documentation** for public APIs

### Blazor Components

- Use **@rendermode InteractiveServer** for interactive components
- Keep components **small and focused**
- Use **cascading parameters** for shared state
- Prefer **EventCallback** over events

### Database

- Use **EF Core migrations** for schema changes
- Follow **repository pattern** where applicable
- Use **tenant-scoped queries** with global filters
- Write **efficient LINQ queries**

### Testing

- Maintain **>80% code coverage**
- Write **unit tests** for business logic
- Write **integration tests** for API endpoints
- Use **xUnit** for .NET tests
- Use **bUnit** for Blazor component tests
- Mock external dependencies (Stripe, email, storage)

## 🧪 Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test SportRental.Api.Tests/

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "FullyQualifiedName~YourTestName"
```

## 📝 Commit Message Guidelines

We follow [Conventional Commits](https://www.conventionalcommits.org/):

### Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types

- **feat**: New feature
- **fix**: Bug fix
- **docs**: Documentation changes
- **style**: Code style changes (formatting, etc.)
- **refactor**: Code refactoring
- **test**: Adding/updating tests
- **chore**: Maintenance tasks

### Examples

```
feat(payments): add Stripe webhook support

- Implement webhook endpoint
- Add signature verification
- Handle payment_intent.succeeded event

Closes #123
```

```
fix(rentals): resolve timezone issue in date calculations

Previously, rental periods were calculated incorrectly when
crossing DST boundaries.

Fixes #456
```

## 🎯 Areas for Contribution

Looking for where to start? Check out issues labeled:

- `good first issue` - Perfect for newcomers
- `help wanted` - We need community help
- `enhancement` - New features to implement
- `bug` - Bugs to fix

## 💬 Questions?

- Open a **GitHub Discussion** for general questions
- Join our **community chat** (if available)
- Tag **@maintainers** in issues for urgent matters

## 🙏 Thank You!

Your contributions make SportRental better for everyone. We appreciate your time and effort!

---

**Happy Coding!** 🚀
