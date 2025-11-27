using Microsoft.Playwright;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SportRental.E2ETests;

[TestFixture]
public class ClientDiagnosticsTest : BaseTest
{
    [Test]
    public async Task CheckClientStartup_ConsoleErrors()
    {
        Console.WriteLine("\n🔍 DIAGNOZA STARTU KLIENTA WASM\n");
        
        var consoleMessages = new List<string>();
        var consoleErrors = new List<string>();
        var consoleWarnings = new List<string>();
        
        Page.Console += (_, msg) =>
        {
            var text = $"[{msg.Type}] {msg.Text}";
            consoleMessages.Add(text);
            
            if (msg.Type == "error")
                consoleErrors.Add(text);
            else if (msg.Type == "warning")
                consoleWarnings.Add(text);
                
            Console.WriteLine($"   Console: {text}");
        };
        
        Page.PageError += (_, error) =>
        {
            Console.WriteLine($"   ❌ PAGE ERROR: {error}");
            consoleErrors.Add($"PAGE ERROR: {error}");
        };
        
        try
        {
            Console.WriteLine("1️⃣ Ładuję stronę główną...");
            await Page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
            
            Console.WriteLine("\n2️⃣ Czekam 3 sekundy na Blazor WASM...");
            await Task.Delay(3000);
            
            Console.WriteLine("\n3️⃣ Robię screenshot...");
            await TakeScreenshotAsync("client_startup");
            
            Console.WriteLine("\n4️⃣ Sprawdzam DOM...");
            var bodyText = await Page.Locator("body").InnerTextAsync();
            Console.WriteLine($"   Body text (pierwsze 200 znaków): {bodyText.Substring(0, Math.Min(200, bodyText.Length))}");
            
            Console.WriteLine("\n5️⃣ Sprawdzam czy są błędy...");
            var mudAlert = Page.Locator(".mud-alert-message");
            if (await mudAlert.CountAsync() > 0)
            {
                var alertText = await mudAlert.First.InnerTextAsync();
                Console.WriteLine($"   ⚠️ ZNALEZIONO ALERT: {alertText}");
            }
            
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("📊 PODSUMOWANIE:");
            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"✅ Wszystkie wiadomości konsoli: {consoleMessages.Count}");
            Console.WriteLine($"⚠️  Ostrzeżenia: {consoleWarnings.Count}");
            Console.WriteLine($"❌ Błędy: {consoleErrors.Count}");
            
            if (consoleWarnings.Any())
            {
                Console.WriteLine("\n⚠️  OSTRZEŻENIA:");
                foreach (var warn in consoleWarnings.Take(10))
                {
                    Console.WriteLine($"   {warn}");
                }
            }
            
            if (consoleErrors.Any())
            {
                Console.WriteLine("\n❌ BŁĘDY:");
                foreach (var err in consoleErrors)
                {
                    Console.WriteLine($"   {err}");
                }
            }
            
            Console.WriteLine(new string('═', 60) + "\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ WYJĄTEK: {ex.Message}");
            await TakeScreenshotAsync("client_startup_error");
            throw;
        }
    }
}








