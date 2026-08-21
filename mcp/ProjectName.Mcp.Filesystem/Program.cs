// mcp/ProjectName.Mcp.Filesystem/Program.cs
// MCP Filesystem Server — read/list/search files in sandbox root

Console.WriteLine("MCP Filesystem Server starting...");
Console.WriteLine($"Sandbox root: {Environment.GetEnvironmentVariable("Filesystem__SandboxRoot") ?? "not set"}");
await Task.Delay(-1); // keep-alive for stdio MCP transport