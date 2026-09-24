# Print Planner

C#12与ASP.NET Core 8基础项目，仅实现GET /healthz健康检查。拼版功能尚未实现。

工具链由global.json固定为.NET SDK8.0.421。本机SDK位置为/home/hj/.local/share/cc-codex/toolchains/dotnet-8.0.421-case006，通用codex入口已为其启动的子进程设置DOTNET_ROOT和PATH。

构建：dotnet build PrintPlanner.sln

现有健康检查：dotnet test PrintPlanner.sln

启动：dotnet run --project src/PrintPlanner.Api -- --urls http://127.0.0.1:0

端口0由系统分配可用端口，实际地址见启动输出。需要固定端口时可通过同一个--urls参数设置。
