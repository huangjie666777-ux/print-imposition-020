# Print Planner

C#12与ASP.NET Core 8基础项目，仅实现GET /healthz，拼版功能尚未实现。

当前目录的.tools/dotnet/dotnet为独立.NET SDK8.0.421，global.json锁定版本；NuGet依赖位于当前目录.packages，NuGet.Config使用相对路径，packages.lock.json锁定依赖。工具和依赖均为独立文件，未提交二进制到Git。

在本项目目录执行以下环境设置及命令，不使用全局或其他题的项目工具：
```sh
export DOTNET_ROOT="$PWD/.tools/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export NUGET_PACKAGES="$PWD/.packages"
./.tools/dotnet/dotnet restore PrintPlanner.sln --locked-mode
./.tools/dotnet/dotnet build PrintPlanner.sln --no-restore
./.tools/dotnet/dotnet test PrintPlanner.sln --no-restore
./.tools/dotnet/dotnet run --project src/PrintPlanner.Api -- --urls http://127.0.0.1:0
```

端口0由系统分配，实际地址见启动输出，可用--urls指定可用端口。只克隆Git时，按toolchain.json的官方版本和来源把SDK解压到.tools/dotnet，再执行上述restore恢复.packages。预备工作区已包含工具和依赖，无需重复安装。
