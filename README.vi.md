# 765T-Forge

<!-- mcp-name: io.github.meococ/765t-forge -->

[English](README.md) | Tiếng Việt

MCP server viết hoàn toàn bằng C#, kèm plugin AutoCAD, phục vụ **sản xuất bản vẽ metro/AEC và phát hành bộ hồ sơ (issue set)**. Agent làm việc với bản vẽ qua typed tool thay vì gõ command: kiểm tra → sửa → điền thông tin → chặn lỗi → publish → kiểm tra lại.

Dự án không liên quan đến Autodesk. Cần AutoCAD có bản quyền. Xem [NOTICE.md](NOTICE.md).

## Giới thiệu

- **125 typed tool, chia theo toolset.** Mặc định mỗi phiên chỉ mở khoảng 19 tool lõi và tool tìm kiếm (khoảng 3,7k token cho `tools/list`). Agent tự mở thêm khi cần bằng `forge_toolset_search`/`forge_toolset_enable`, hoặc đặt `FORGE_TOOLSETS=all` để có đủ tool ngay từ đầu.
- **Plot và publish thật:** plot cấu hình được device, khổ giấy, CTB/STB, vùng in, tỉ lệ; publish nhiều layout bằng DSD, có bookmark PDF và bước kiểm tra lại.
- **Có bước chặn lỗi, không chỉ có tool vẽ:** `forge_qa_preflight` có thể chặn publish khi xref, khung tên, field hiện `####` hoặc standards pack bị lỗi; `forge_qa_native_objects` phát hiện dim, hatch, block đã bị explode trong file nguồn dùng lại; `forge_qa_annotation_clash` tìm chữ đè nhau và nét cắt qua chữ.
- **Tool vẽ và chỉnh sửa:** `forge_draw_*` (từ line tới NURBS spline, dim, hatch, table, MLeader, tường, lưới trục), `forge_entity_*` sửa đối tượng theo handle chỉ định, block và attribute, quản lý layer/layout/style.
- **MCP core v2:** mọi tool trả structured output; có progress notification, elicitation để xác nhận thao tác xóa hoặc ghi đè, MCP task, resource `forge://` đọc trực tiếp từ bản vẽ đang mở, gợi ý tham số (completion) và hủy lệnh thật.
- **An toàn:** denylist cho các executor open-world, kiểm tra safety ở cả server lẫn plugin, mọi lệnh ghi đều có dry-run, sao lưu DWG trước khi ghi, audit JSONL, named pipe có ACL và token bắt buộc, unsafe ops tắt mặc định, lỗi trả về có `retryable` + `hint` để agent không thử lại vô ích.

Kết quả đo trên chính codebase này: 32 lần chạy agent (mỗi lần một phiên mới, chỉ dùng MCP) trên 8 bài vẽ **giả lập** đều đạt toàn bộ tiêu chí chấm v2. Bài giả lập chỉ cho thấy tool hoạt động đúng và agent dùng tool đúng cách; chưa phải cam kết về kết quả trên bản vẽ thật của bạn.

## Phiên bản đã kiểm tra

| AutoCAD | Trạng thái |
|---------|-----------|
| 2025 | **Đã test trực tiếp** trên AutoCAD 2025 đang chạy |
| 2026 | **Build được, chưa test trực tiếp**: biên dịch được với thư viện managed của 2026, chưa chạy thử trong AutoCAD 2026 |

## Yêu cầu

- Windows + .NET 8 SDK
- AutoCAD 2025 (đã test) hoặc 2026 (build được) để chạy plugin và các tool cần AutoCAD
- `FORGE_AUTOCAD_TOKEN` (hoặc `MCP_AUTOCAD_TOKEN`): **bắt buộc**, không có token mặc định cho môi trường làm việc thật

## Cài đặt nhanh

### 1. Build và cài plugin (trên máy có AutoCAD)

```powershell
.\scripts\build-plugin.ps1 -Configuration Release -Year 2025
.\scripts\install-plugin.ps1 -Configuration Release -Year 2025 -SkipBuild
```

Thư mục cài mặc định: `%LOCALAPPDATA%\765T-Forge\plugin\<Year>\Forge.Plugin.dll`.

Sau đó:

1. Thêm thư mục cài vào **Trusted Locations** của AutoCAD (`TRUSTEDPATHS`).
2. Đặt `FORGE_AUTOCAD_TOKEN` (và `FORGE_PIPE_NAME` nếu cần) cho **tiến trình AutoCAD**; giá trị phải khớp với MCP server.
3. `NETLOAD` file DLL đã cài, rồi chạy lệnh `MCP_STATUS` trong AutoCAD.
4. Từ MCP client, gọi `forge_system_health`; trường `pipe` phải trùng với tên pipe đã cấu hình.

**Hoặc dùng Autoloader:** chép `plugin-bundle/765T-Forge.bundle` vào `%APPDATA%\Autodesk\ApplicationPlugins` (cho từng user, nên dùng) hoặc `%PROGRAMFILES%\Autodesk\ApplicationPlugins` (cho cả máy, cần quyền admin). Trên 2026 không nên dùng `%PROGRAMDATA%` vì Autodesk đã siết việc tự nạp plugin từ thư mục này. Mỗi năm AutoCAD cần build và bundle riêng: 2025 = Series R25.0, 2026 = R25.1.

**Bản phát hành:** runner của GitHub không có AutoCAD nên CI chỉ build được zip **server**; zip plugin được build trên máy có AutoCAD rồi đính kèm vào cùng bản phát hành. Muốn dùng được cần tải cả `765T-Forge.Server-win-x64.zip` và `765T-Forge.Plugin.zip`.

### 2. Khai báo Forge.Server.exe trong MCP client

Khối `env` giống nhau ở mọi client; chỉ khác file cấu hình và tên key.

```json
{
  "command": "C:\\path\\to\\765T-Forge\\src\\Forge.Server\\bin\\Release\\net8.0\\Forge.Server.exe",
  "args": [],
  "env": {
    "FORGE_AUTOCAD_TOKEN": "replace-with-a-long-random-secret",
    "FORGE_PIPE_NAME": "765T.Forge.AutoCAD",
    "FORGE_TOOLSETS": "all",
    "FORGE_BACKUP_DIR": "%LOCALAPPDATA%\\765T-Forge\\backups",
    "FORGE_AUDIT_DIR": "%LOCALAPPDATA%\\765T-Forge\\audit"
  }
}
```

Nếu dùng bản phát hành, trỏ `command` tới `Forge.Server.exe` trong thư mục đã giải nén.

- **Claude Code**: thêm vào `mcpServers."765t-forge"` trong file `.mcp.json` của project, hoặc chạy:
  `claude mcp add-json 765t-forge '{"command":"...","env":{...}}'`
- **Claude Desktop**: thêm cùng khối `mcpServers` vào `%APPDATA%\Claude\claude_desktop_config.json`, rồi thoát hẳn và mở lại app.
- **Cursor**: cùng khối `mcpServers` trong file `mcp.json` của user hoặc của project.
- **VS Code (Copilot)**: cùng cấu trúc nhưng đặt dưới key `servers` trong `.vscode/mcp.json`, thêm `"type": "stdio"`. Nên khai báo token qua `inputs` kiểu `promptString` để token không nằm trong file commit lên git.

`FORGE_TOOLSETS=all` hiện đủ tool ngay từ đầu; bỏ biến này để dùng bộ tool mặc định gọn nhẹ (client hỗ trợ `tools/list_changed` sẽ tự mở thêm khi cần).

### `dnx` (khi đã có gói NuGet)

```powershell
dnx Forge765T.Server@<pinned-version> --yes
```

Luôn ghi rõ version; không dùng `@latest`.

## Cấu hình

| Biến | Ghi chú |
|---|---|
| `FORGE_AUTOCAD_TOKEN` / `MCP_AUTOCAD_TOKEN` | **Bắt buộc.** Mã bí mật dùng chung cho named pipe; phải giống nhau ở tiến trình server và AutoCAD |
| `FORGE_DEV_ALLOW_DEFAULT_TOKEN` | Chỉ dùng khi phát triển (`true` → dùng token mặc định, không an toàn); không dùng trên máy làm việc thật |
| `FORGE_PIPE_NAME` | Mặc định `765T.Forge.AutoCAD` |
| `FORGE_BACKUP_DIR` / `FORGE_AUDIT_DIR` | Mặc định nằm trong `%LOCALAPPDATA%\765T-Forge\` |
| `AUTOCAD_2026_ROOT` | Đường dẫn cài AutoCAD (dùng cho reference của plugin và accoreconsole) |
| `FORGE_ENABLE_UNSAFE_OPS` | Bật `forge_exec_dotnet`; mỗi lần gọi vẫn phải xác nhận thêm |
| `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` | Mặc định `120`; watchdog của plugin (`plugin_request_timeout`) dừng ở mức timeout − 5 giây |
| `FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS` | Mặc định `10` (giới hạn 1–120) |
| `FORGE_TOOLSETS` | Danh sách cách nhau bằng dấu phẩy, hoặc `all`; mặc định nạp tool lõi + tool tìm kiếm (~19 tool ≈ 3,7k token) |

## Danh sách tool

125 tool chia theo nhóm. Resource MCP `forge://docs/capability-matrix` được sinh tự động từ registry nên luôn khớp với bản đang chạy.

| Nhóm | Ví dụ |
|---|---|
| system / document | `forge_system_health`, `forge_system_version`, `forge_doc_open`/`_save`/`_close`/`_activate`, `forge_doc_list_open`, `forge_doc_new`, `forge_doc_import` (DXF/DWG → block/merge/xref), `forge_doc_export_dxf` |
| layer / layout / viewport | `forge_layer_list`, `forge_layer_create`, `forge_layer_delete` (mặc định chạy thử; không xóa layer 0, layer hiện hành, layer của xref hay layer đang dùng), `forge_layer_state_*`, `forge_layout_create`, `forge_layout_page_setup_import`/`_apply`, `forge_viewport_add`/`_list`, `forge_viewport_set_layer_freeze` |
| draw / entity | `forge_draw_line…revcloud` (15 loại đối tượng, gồm spline, hatch, dimension, table, leader, lưới trục, tường), `forge_draw_batch` (≤1000 bước), `forge_entity_get`/`_list`, `forge_entity_move`/`copy`/`rotate`/`scale`/`mirror`/`offset`, `forge_entity_fillet`/`chamfer`/`trim`/`extend`/`join`/`explode`, `forge_entity_erase`/`set_props`/`array`/`intersect`/`snap`, `forge_undo` |
| block / style | `forge_block_list`/`_get_attr`/`_set_attr`/`_campaign`, `forge_block_create`/`_insert`/`_insert_bulk`, `forge_linetype_load`, `forge_style_text_create`, `forge_style_dim_create`, `forge_standards_import`, `forge_text_list` |
| plot / publish | `forge_plot_to_pdf` (cấu hình device/khổ giấy/CTB/vùng in/tỉ lệ, không mở trình xem PDF), `forge_plot_preset_apply`, `forge_plot_publish` (DSD thật + bookmark + chặn bằng preflight) |
| qa | `forge_qa_preflight`, `forge_qa_native_objects` (dim/hatch/block bị explode), `forge_qa_annotation_clash`, `forge_qa_verify_titleblock`, `forge_qa_check_xrefs`, `forge_qa_audit_layers`, `forge_qa_readback`, `forge_qa_readback_after_timeout`, `forge_audit_summarize` |
| recipe / registry / standards | `forge_recipe_issue_set`, `forge_issue_set_validate`/`_diff`, `forge_sheet_inventory_import`, `forge_registry_load`/`_lookup`, `forge_pack_load`/`_status` |
| linework | `forge_linework_dump`/`_trace`/`_topology`/`_coverage`/`_segments`/`_compare`/`_transform`: so khớp nét CAD với model (mốc lấy từ phía Revit) |
| conversion / batch | `forge_dgn_import` (DGN V8 → DWG, có kiểm tra), `forge_pack_and_go`, `forge_batch_run`/`_status` (chạy hàng loạt bằng AccoreConsole, chạy tiếp được khi bị ngắt) |
| capture / measure | `forge_view_capture` (chụp PNG cửa sổ AutoCAD để agent xem lại kết quả), `forge_measure_batch`, `forge_drawing_summary`, `forge_entity_search_text` |
| exec (open-world, có denylist) | `forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet` (phải bật unsafe) |
| meta (luôn bật) | `forge_toolset_list`, `forge_toolset_search`, `forge_toolset_enable` |

Còn hạn chế: `forge_exec_lisp` chạy đồng bộ qua pipe bị giới hạn bởi command context (chế độ xếp hàng vẫn chạy); `forge_layout_page_setup_import` nên dùng cách copy PlotSettings đồng bộ, vì cách dự phòng `-PSETUPIN` xếp hàng không kiểm soát được hộp thoại proxy. Chưa hỗ trợ: ghi đầy đủ vào SSM, Civil 3D, và phục vụ nhiều người dùng qua mạng.

## An toàn (tóm tắt)

- Lệnh gửi qua executor open-world được lọc bằng denylist regex (`ERASE ALL`, `PURGE`, chọn rộng `ALL`/`*`, …). Typed tool không cần quét; tool lạ mặc định bị coi là thao tác phá hủy và bị chặn.
- Tool đi qua pipe được kiểm tra safety ở **cả** server và plugin; `forge_run_script`/`forge_batch_run` chạy headless nên chỉ kiểm tra ở server.
- Mọi lệnh ghi đều nhận `dryRun=true`. Lệnh làm thay đổi DWG sẽ sao lưu file vào `FORGE_BACKUP_DIR` trước; mọi lần gọi đều ghi audit JSONL vào `FORGE_AUDIT_DIR`.
- Ghi đè file có sẵn phải kèm `overwriteAcknowledged=true`; `forge_doc_close` không đóng file còn thay đổi chưa lưu, trừ khi được yêu cầu rõ; `forge_layer_delete` mặc định chỉ chạy thử và không xóa layer được bảo vệ hoặc đang dùng.
- Nếu lệnh gửi plugin bị timeout, thay đổi có thể đã được ghi; hãy đọc lại bằng `forge_qa_*` thay vì gửi lại. `plugin_request_timeout`/`plugin_request_stale` giới hạn khoảng thời gian này.
- Lỗi trả về có `retryable` + `hint`: lỗi kiểm tra dữ liệu và lỗi trong plugin là loại thử lại cũng không được; hãy đổi input hoặc báo lỗi, đừng gửi lại y nguyên.
- `forge_exec_dotnet` cần cả `FORGE_ENABLE_UNSAFE_OPS=true` **và** `unsafeAcknowledged=true` trong từng lần gọi. Không có sandbox.

## Xử lý sự cố

- **Mất kết nối pipe / health lỗi:** AutoCAD có đang mở không? Plugin đã `NETLOAD` và `MCP_STATUS` báo OK chưa? `FORGE_PIPE_NAME` và `FORGE_AUTOCAD_TOKEN` có giống nhau ở cả hai tiến trình không? Mỗi máy chỉ nên có một pipe của plugin; nếu phiên trước bị treo, khởi động lại AutoCAD.
- **NETLOAD bị chặn:** DLL phải nằm trong `TRUSTEDPATHS`; dùng `install-plugin.ps1` và làm theo checklist script in ra.
- **DLL bị khóa khi AutoCAD đang mở:** sau khi NETLOAD, Windows khóa `Forge.Plugin.dll`; hãy build bằng `765T-Forge.ServerOnly.slnf`, hoặc dùng `-p:OutDir=` ra thư mục tạm, hoặc đóng AutoCAD trước khi ghi đè.
- **Thiếu token / bị từ chối xác thực:** đặt `FORGE_AUTOCAD_TOKEN` trong env của MCP client **và** cho tiến trình AutoCAD, khởi động lại AutoCAD rồi NETLOAD lại.
- **Timeout sau lệnh ghi:** coi như thay đổi có thể đã được ghi; gọi `forge_qa_readback_after_timeout` trước khi thử lại.
- **File phát hành:** tải **cả hai** file zip ở trang release.

## Build từ source

```powershell
$env:FORGE_DEV_ALLOW_DEFAULT_TOKEN = "true"   # chỉ dùng cho test / máy cá nhân
dotnet restore .\765T-Forge.ServerOnly.slnf
dotnet build   .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test    .\765T-Forge.ServerOnly.slnf --no-build
```

Solution đầy đủ (cần thư viện của AutoCAD; dùng `AUTOCAD_2026_ROOT` để đổi đường dẫn mặc định):

```powershell
dotnet build .\765T-Forge.sln
dotnet test  .\765T-Forge.sln
```

Script: `scripts\build-server.ps1`, `build-plugin.ps1`, `install-plugin.ps1`, `pack-release.ps1`, `deploy-server.ps1` (deploy vào thư mục riêng, có kiểm tra SHA256), `launch-acad-instance.ps1`/`stop-acad-instance.ps1` (chạy nhiều AutoCAD song song).

## Đóng góp

- Sửa phía server: giữ `dotnet test .\765T-Forge.ServerOnly.slnf` luôn pass; CI chạy trên `windows-latest` với `--locked-mode`.
- Sửa plugin: build theo từng năm bằng `scripts\build-plugin.ps1 -Year` và ghi rõ đã NETLOAD test những gì.
- Thêm tool mới phải sửa đồng bộ **ba** chỗ: method `[McpServerTool]` trong `src/Forge.Server/ForgeMcpTools.cs`, entry `ForgeToolRegistry` trong `src/Forge.Shared/ToolMetadata.cs`, và case dispatch trong `src/Forge.Plugin/PluginCommandProcessor.cs`. Tool chưa đăng ký sẽ bị chặn như thao tác phá hủy.
- Ưu tiên typed tool thay vì executor open-world; giữ nguyên cơ chế kiểm tra safety hai phía; thêm `SafetyPolicyTests` khi có deny code mới.
- Không commit token, file DWG của khách hàng hay binary của Autodesk. File `*.log` không đưa vào git.

## Báo lỗ hổng bảo mật

Vui lòng báo riêng, không mở issue công khai. Dùng **GitHub Security Advisories** (nút "Report a vulnerability" của repo). Ghi rõ thành phần bị ảnh hưởng (`Forge.Server`, `Forge.Plugin`, giao thức pipe), các bước tái hiện và mức độ ảnh hưởng. Xin chờ một khoảng thời gian hợp lý để sửa trước khi công bố.

Về ranh giới tin cậy: ACL của pipe chỉ cho phép user hiện tại và LocalSystem; token là lớp bảo vệ thêm, nên đặt `FORGE_AUTOCAD_TOKEN` đủ dài và không dùng lại ở nơi khác. `forge_exec_dotnet` là đường rủi ro cao nhất và luôn cần hai lớp xác nhận.

## Phiên bản

Version sản phẩm theo SemVer **0.3.0** (git tag + GitHub Release), độc lập với version giao thức MCP và năm AutoCAD. Plugin build cho năm AutoCAD nào chỉ chạy với năm đó; mỗi năm phải build riêng.

## Giấy phép

MIT: [LICENSE](LICENSE). Ghi nhận bên thứ ba: [NOTICE.md](NOTICE.md). Repo **không** phân phối lại API hay DLL của Autodesk.
