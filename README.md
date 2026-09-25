# Print Planner

C#12 / ASP.NET Core 8 标签拼版服务：将混合尺寸标签以毫米为单位确定性地排到纸张上，并提供可浏览的 SVG 预览。

当前目录的.tools/dotnet/dotnet为独立.NET SDK8.0.421，global.json锁定版本；NuGet依赖位于当前目录.packages，NuGet.Config使用相对路径，packages.lock.json锁定依赖。工具和依赖均为独立文件，未提交二进制到Git。

## 接口

- `GET /healthz`：健康检查，返回 `{ "status": "ok" }`。
- `POST /api/plans`：提交拼版请求，返回 JSON 规划。
- `POST /api/plans/preview`：请求格式相同，返回 `image/svg+xml` 预览，可直接在浏览器打开。

### 请求格式（单位：毫米，可带小数）

```json
{
  "paperWidth": 210,
  "paperHeight": 297,
  "marginTop": 8,
  "marginRight": 8,
  "marginBottom": 8,
  "marginLeft": 8,
  "gap": 2,
  "bleed": 1.5,
  "maxSheets": 10,
  "labels": [
    { "id": "name-tag", "width": 50, "height": 30, "quantity": 100, "allowRotation": true }
  ]
}
```

约束与校验：纸张尺寸必须为正数；留白、`gap`、`bleed` 为非负数；`maxSheets` 和 `quantity` 为正整数；四边留白后必须仍有正的可印区；`id` 必填且不可重复。任意条件不满足都返回 `400` 与逐字段 `errors: [{ field, message }]`，不生成任何部分方案。

### 响应

坐标系原点在纸张左下角，`x` 向右、`y` 向上，与 SVG 中的位置一一对应（SVG 渲染时做纵向翻转）。每个出血框必须完整位于留白围成的可印区内；任意两个出血框不得重叠，且在**水平或垂直方向至少留足 `gap`**；纸张边缘不额外加间距。全部比较使用 `decimal`，不靠舍入判断越界或重叠。

```json
{
  "sheets": [
    {
      "index": 1,
      "instances": [
        {
          "sourceId": "name-tag",
          "sequence": 1,
          "cutBox": { "x": 9.5, "y": 9.5, "width": 50, "height": 30 },
          "bleedBox": { "x": 8, "y": 8, "width": 53, "height": 33 },
          "rotated": false
        }
      ]
    }
  ],
  "unplaced": [
    { "sourceId": "name-tag", "sequence": 101, "reason": "sheet_limit: ..." }
  ],
  "sheetsUsed": 1
}
```

- `sequence`：同一来源标签的 1 基序号，每个实例恰好出现一次（在纸上或 `unplaced` 中）。
- 未安排原因：`too_large`（含出血在单张可印区两个方向都放不下）或 `sheet_limit`（已打开纸张放不下且已达 `maxSheets`）。
- SVG 预览：黑线为裁切框，红色虚线为出血框，蓝色虚线为留白/可印区边界；标签文字（`id#序号`，旋转带 `R`）经过 XML 转义。

## 排样取舍

算法为确定性的 MaxRects / Best Short Side Fit 启发式：

1. 实例按请求中的标签顺序和序号处理；
2. 放置前先在所有**已打开纸张**中找最佳位置（按纸张打开顺序），放不下才新增纸张，达到 `maxSheets` 后未安排的实例记入 `unplaced`；
3. 候选位置按“自由矩形剩余短边最小 → 剩余长边最小 → 不旋转优先 → 靠下 → 靠左”排序，不使用随机数或哈希集合遍历顺序，相同请求产生完全相同的布局；
4. 允许旋转时同时评估 0°/90°，旋转同时改变裁切框与出血框（宽高互换）；
5. 排样是启发式，优先稳定与紧凑，不保证全局用纸最优。

## 构建与运行

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
