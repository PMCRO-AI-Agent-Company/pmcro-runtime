// mcp/ProjectName.Mcp.Playwright/Program.cs
// MCP Playwright Server — browser automation, lazy actuator (not in WaitFor chain)

Console.WriteLine("MCP Playwright Server starting...");
var headless = Environment.GetEnvironmentVariable("Playwright__Headless") ?? "true";
Console.WriteLine($"Headless: {headless}");
await Task.Delay(-1);