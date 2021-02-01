cd src/Microsoft.AspNet.SignalR.Core.NetCore

nuget pack Microsoft.AspNet.SignalR.Core.NetCore.csproj -Properties Configuration=Release -OutputDirectory bin

nuget push bin/Microsoft.AspNet.SignalR.Core.NetCore.1.0.22.nupkg 2F3601FE-7B72-4BDD-B983-2CB9D5F9D042 -Source http://nuget.flexem.net/nuget

pause