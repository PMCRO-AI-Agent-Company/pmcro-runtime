// mcp/ProjectName.Mcp.Terminal/Program.cs
// MCP Terminal Server — execute shell commands in working root

Console.WriteLine("MCP Terminal Server starting...");
Console.WriteLine($"Working root: {Environment.GetEnvironmentVariable("Parameters__working-root") ?? "not set"}");
await Task.Delay(-1);