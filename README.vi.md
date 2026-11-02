# 765T-Forge

<!-- mcp-name: io.github.meococ/765t-forge -->

[English](README.md) | Tiếng Việt

MCP server viết hoàn toàn bằng C# + plugin AutoCAD cho **quy trình sản xuất bản vẽ metro/AEC (issue-set)** — inspect → fix → fill → gate → publish → verify, qua các typed tool thay vì command string.

Không liên kết với Autodesk. Cần bản quyền AutoCAD. Xem [NOTICE.md](NOTICE.md).

## Sản phẩm là gì

- **125 typed tool**, nằm sau bộ lọc toolset. Phiên mặc định chỉ mở ~19 tool lõi/khám phá (~3,7k token trong `tools/list`) — agent mở rộng lúc chạy bằng `forge_toolset_search`/`forge_toolset_enable`, hoặc đặt `FORGE_TOOLSETS=all` để liệt kê toàn bộ catalog ngay từ đầu.
- **Quy trình sản xuất thật:** plot có cấu hình (device/paper/CTB·STB/area/scale) và publish DSD multi-layout thật với PDF bookmark + verify.
- **Cổng chất lượng, không chỉ tool vẽ:** `forge_qa_preflight` có thể từ chối publish khi lỗi xref, titleblock, trường `####` hoặc standards-pack; `forge_qa_native_objects` phát hiện dimension/hatch/block đã bị explode trong nguồn tái sử dụng; `forge_qa_annotation_clash` tìm text chồng lấn và linework cắt ngang text.
- **Bề mặt authoring:** `forge_draw_*` (từ line đến NURBS spline, dimension, hatch, table, MLeader, wall, lưới trục), `forge_entity_*` chỉnh sửa bằng handle tường minh, block/attribute, quản lý layer/layout/style.
- **MCP core v2:** structured output cho mọi tool, progress notification, elicitation khi cần xác nhận thao tác phá hủy, MCP task, live `forge://` drawing resource, argument completion, cancellation thật.
- **Mô hình an toàn:** denylist cho executor open-world, đánh giá safety kép (server + plugin), dry-run cho mọi write, backup DWG trước khi ghi, audit JSONL, named-pipe ACL + token bắt buộc, unsafe ops tắt mặc định, metadata `retryable`+`hint` trong kết quả lỗi giúp tránh retry mù.

Con số đo trên codebase này: một outcome eval gồm 32 run (mỗi run một phiên agent mới, chỉ dùng MCP) trên 8 task bản vẽ **giả lập (synthetic)** đạt toàn bộ kiểm tra v2. Task synthetic đo plumbing và tính kỷ luật — không phải cam kết với bản vẽ thật của bạn.

## Phiên bản đã kiểm chứng

| AutoCAD | Trạng thái |
|---------|-----------|
| 2025 | **Đã live-test** — toàn bộ bề mặt tool được chạy trên host 2025 đang hoạt động |
| 2026 | **Build được, chưa live-test** — biên dịch với managed reference của 2026; chưa chứng minh trong phiên thật |

## Yêu cầu

- Windows + .NET 8 SDK
- AutoCAD 2025 (đã live-test) hoặc 2026 (build được) cho plugin / các tool live
- `FORGE_AUTOCAD_TOKEN` (hoặc `MCP_AUTOCAD_TOKEN`) — **bắt buộc**, không có secret mặc định cho production

## Cài nhanh

### 1. Build và cài plugin (trên máy có AutoCAD)

```powershell
.\scripts\build-plugin.ps1 -Configuration Release -Year 2025
.\scripts\install-plugin.ps1 -Configuration Release -Year 2025 -SkipBuild
```

Thư mục cài mặc định: `%LOCALAPPDATA%\765T-Forge\plugin\<Year>\Forge.Plugin.dll`.

Sau đó:

1. Thêm thư mục cài vào **Trusted Locations** của AutoCAD (`TRUSTEDPATHS`).
2. Đặt `FORGE_AUTOCAD_TOKEN` (và `FORGE_PIPE_NAME` nếu cần) cho **process AutoCAD** — phải khớp với MCP server.
3. `NETLOAD` DLL đã cài, rồi chạy `MCP_STATUS` trong AutoCAD.
4. Từ phía MCP client, gọi `forge_system_health` — trường `pipe` phải khớp tên pipe đã cấu hình.

**Cách khác — Autoloader:** copy `plugin-bundle/765T-Forge.bundle` vào `%APPDATA%\Autodesk\ApplicationPlugins` (per-user; khuyên dùng) hoặc `%PROGRAMFILES%\Autodesk\ApplicationPlugins` (toàn máy, cần admin). Tránh `%PROGRAMDATA%` trên 2026 — Autodesk đã siết auto-load từ ProgramData. Build/bundle theo năm: 2025 = Series R25.0, 2026 = R25.1.

**Release:** CI trên GitHub-hosted runner chỉ pack **server** zip (không có AutoCAD trên runner). Một bản cài dùng được cần cả `765T-Forge.Server-win-x64.zip` lẫn `765T-Forge.Plugin.zip`.

### 2. Trỏ MCP client tới Forge.Server.exe

Khối `env` giống nhau cho mọi client; chỉ khác file cấu hình và tên key.

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

- **Claude Code** — đặt vào `mcpServers."765t-forge"` trong `.mcp.json` của project, hoặc:
  `claude mcp add-json 765t-forge '{"command":"...","env":{...}}'`
- **Claude Desktop** — cùng khối `mcpServers` trong `%APPDATA%\Claude\claude_desktop_config.json`; thoát hẳn rồi mở lại app.
- **Cursor** — cùng khối `mcpServers` trong `mcp.json` của user hoặc project.
- **VS Code (Copilot)** — cùng dạng nhưng nằm dưới key `servers` trong `.vscode/mcp.json`, kèm `"type": "stdio"`. Để token vào `inputs` `promptString` để không nằm trong file đã check-in.

`FORGE_TOOLSETS=all` liệt kê toàn bộ tool ngay; bỏ trống để dùng bề mặt mặc định tối thiểu (client hỗ trợ `tools/list_changed` sẽ tự mở rộng khi cần).

### `dnx` (khi gói NuGet đã có)

```powershell
dnx Forge765T.Server@<pinned-version> --yes
```

Luôn ghim version; không bao giờ dùng `@latest`.

## Cấu hình

| Biến | Ghi chú |
|---|---|
| `FORGE_AUTOCAD_TOKEN` / `MCP_AUTOCAD_TOKEN` | **Bắt buộc** shared secret cho named pipe; phải khớp trên cả process server lẫn AutoCAD |
| `FORGE_DEV_ALLOW_DEFAULT_TOKEN` | Cửa thoát chỉ cho dev (`true` → token không an toàn); không dùng trong production |
| `FORGE_PIPE_NAME` | Mặc định `765T.Forge.AutoCAD` |
| `FORGE_BACKUP_DIR` / `FORGE_AUDIT_DIR` | Mặc định dưới `%LOCALAPPDATA%\765T-Forge\` |
| `AUTOCAD_2026_ROOT` | Ghi đè thư mục cài AutoCAD (reference của plugin + accoreconsole) |
| `FORGE_ENABLE_UNSAFE_OPS` | Bật `forge_exec_dotnet` khi được acknowledge thêm trong từng call |
| `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` | Mặc định `120`; cũng điều khiển watchdog của plugin (`plugin_request_timeout`) ở timeout−5 s |
| `FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS` | Mặc định `10` (clamp 1–120) |
| `FORGE_TOOLSETS` | Danh sách phân cách bằng dấu phẩy hoặc `all`; mặc định nạp tool lõi + khám phá (~19 tool ≈3,7k token) |

## Danh sách tool

125 tool, chia theo nhóm. Resource MCP `forge://docs/capability-matrix` luôn phản ánh registry hiện hành (sinh tự động, không phải danh sách viết tay).

| Nhóm | Ví dụ |
|---|---|
| system / document | `forge_system_health`, `forge_system_version`, `forge_doc_open`/`_save`/`_close`/`_activate`, `forge_doc_list_open`, `forge_doc_new`, `forge_doc_import` (DXF/DWG→block/merge/xref), `forge_doc_export_dxf` |
| layer / layout / viewport | `forge_layer_list`, `forge_layer_create`, `forge_layer_delete` (mặc định dry-run; từ chối layer 0/current/xref-dependent/đang dùng), `forge_layer_state_*`, `forge_layout_create`, `forge_layout_page_setup_import`/`_apply`, `forge_viewport_add`/`_list`, `forge_viewport_set_layer_freeze` |
| draw / entity | `forge_draw_line…revcloud` (15 primitive gồm spline, hatch, dimension, table, leader, lưới trục, wall), `forge_draw_batch` (≤1000 bước), `forge_entity_get`/`_list`, `forge_entity_move`/`copy`/`rotate`/`scale`/`mirror`/`offset`, `forge_entity_fillet`/`chamfer`/`trim`/`extend`/`join`/`explode`, `forge_entity_erase`/`set_props`/`array`/`intersect`/`snap`, `forge_undo` |
| block / style | `forge_block_list`/`_get_attr`/`_set_attr`/`_campaign`, `forge_block_create`/`_insert`/`_insert_bulk`, `forge_linetype_load`, `forge_style_text_create`, `forge_style_dim_create`, `forge_standards_import`, `forge_text_list` |
| plot / publish | `forge_plot_to_pdf` (cấu hình device/paper/CTB/area/scale, không mở viewer), `forge_plot_preset_apply`, `forge_plot_publish` (DSD thật + bookmark + cổng preflight) |
| qa | `forge_qa_preflight`, `forge_qa_native_objects` (dim/hatch/block bị explode), `forge_qa_annotation_clash`, `forge_qa_verify_titleblock`, `forge_qa_check_xrefs`, `forge_qa_audit_layers`, `forge_qa_readback`, `forge_qa_readback_after_timeout`, `forge_audit_summarize` |
| recipe / registry / standards | `forge_recipe_issue_set`, `forge_issue_set_validate`/`_diff`, `forge_sheet_inventory_import`, `forge_registry_load`/`_lookup`, `forge_pack_load`/`_status` |
| linework | `forge_linework_dump`/`_trace`/`_topology`/`_coverage`/`_segments`/`_compare`/`_transform` — so khớp CAD↔model (anchor phía Revit) |
| conversion / batch | `forge_dgn_import` (DGN V8 → DWG, có verify), `forge_pack_and_go`, `forge_batch_run`/`_status` (hàng đợi AccoreConsole + resume) |
| capture / measure | `forge_view_capture` (chụp PNG cửa sổ AutoCAD — để agent nhìn lại), `forge_measure_batch`, `forge_drawing_summary`, `forge_entity_search_text` |
| exec (open-world, bị denylist) | `forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet` (có cổng unsafe) |
| meta (luôn bật) | `forge_toolset_list`, `forge_toolset_search`, `forge_toolset_enable` |

Các tool còn partial: `forge_exec_lisp` eval đồng bộ bị giới hạn theo command context trên pipe (fallback queued vẫn chạy); `forge_layout_page_setup_import` nên dùng đường copy PlotSettings đồng bộ — fallback `-PSETUPIN` xếp hàng không scope được dialog proxy. SSM write đầy đủ, Civil 3D và serve đa tenant qua mạng nằm ngoài phạm vi.

## Mô hình an toàn (tóm tắt)

- Các executor open-world bị denylist bằng regex (`ERASE ALL`, `PURGE`, chọn rộng `ALL`/`*`, …); typed tool bỏ qua bước quét — tool không rõ mặc định bị coi là destructive.
- Safety được đánh giá trên **cả** server lẫn plugin cho tool qua pipe; `forge_run_script`/`forge_batch_run` headless chỉ đánh giá phía server.
- Mọi write đều nhận `dryRun=true`. Write làm thay đổi DWG sẽ copy file vào `FORGE_BACKUP_DIR` trước; mọi call đều ghi audit JSONL trong `FORGE_AUDIT_DIR`.
- Ghi đè đường dẫn phá hủy cần `overwriteAcknowledged=true`; `forge_doc_close` từ chối khi còn thay đổi chưa lưu trừ khi được cho phép; `forge_layer_delete` mặc định dry-run và từ chối layer được bảo vệ/đang dùng.
- Nếu call plugin timeout, write có thể đã commit — hãy đọc lại (`forge_qa_*`) thay vì retry; `plugin_request_timeout`/`plugin_request_stale` giới hạn cửa sổ đó.
- Lỗi trả về có `retryable`+`hint`: validation và plugin exception là non-retryable — đổi input hoặc báo cáo, đừng lặp lại call y hệt.
- `forge_exec_dotnet` cần `FORGE_ENABLE_UNSAFE_OPS=true` **và** `unsafeAcknowledged=true` trong từng call. Không có sandbox.

## Troubleshooting

- **Pipe chết / health lỗi:** AutoCAD đang mở? Plugin đã `NETLOAD` và `MCP_STATUS` OK? `FORGE_PIPE_NAME` và `FORGE_AUTOCAD_TOKEN` giống hệt trên cả hai process? Chỉ nên có một instance pipe của plugin — khởi động lại AutoCAD nếu phiên trước để lại pipe hỏng.
- **NETLOAD bị chặn:** DLL phải nằm dưới `TRUSTEDPATHS`; dùng `install-plugin.ps1` và làm theo checklist nó in ra.
- **DLL bị khóa khi AutoCAD đang mở:** Windows khóa `Forge.Plugin.dll` sau NETLOAD — build bằng `765T-Forge.ServerOnly.slnf`, hoặc `-p:OutDir=` ra thư mục tạm, hoặc đóng AutoCAD trước khi ghi đè.
- **Thiếu token / bị từ chối auth:** đặt `FORGE_AUTOCAD_TOKEN` trong env của MCP host **và** cho process AutoCAD, khởi động lại AutoCAD, NETLOAD lại.
- **Timeout sau một write:** giả định nó có thể đã commit; gọi `forge_qa_readback_after_timeout` trước khi retry.
- **Artifact release:** CI chỉ pack server — cài đặt dùng được cần **cả hai** zip.

## Build từ source

```powershell
$env:FORGE_DEV_ALLOW_DEFAULT_TOKEN = "true"   # chỉ cho test / local
dotnet restore .\765T-Forge.ServerOnly.slnf
dotnet build   .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test    .\765T-Forge.ServerOnly.slnf --no-build
```

Solution đầy đủ (cần reference của AutoCAD; `AUTOCAD_2026_ROOT` ghi đè thư mục mặc định):

```powershell
dotnet build .\765T-Forge.sln
dotnet test  .\765T-Forge.sln
```

Script: `scripts\build-server.ps1`, `build-plugin.ps1`, `install-plugin.ps1`, `pack-release.ps1`, `deploy-server.ps1` (deploy có kiểm tra SHA), `launch-acad-instance.ps1`/`stop-acad-instance.ps1` (harness chạy nhiều instance song song).

## Đóng góp

- Thay đổi phía server/server-only: giữ `dotnet test .\765T-Forge.ServerOnly.slnf` xanh; CI chạy trên `windows-latest` với `--locked-mode`.
- Thay đổi plugin: build theo năm qua `scripts\build-plugin.ps1 -Year`; ghi rõ bạn đã NETLOAD-test gì.
- Thêm một tool cần **ba** chỉnh sửa đồng bộ: method `[McpServerTool]` trong `src/Forge.Server/ForgeMcpTools.cs`, entry `ForgeToolRegistry` trong `src/Forge.Shared/ToolMetadata.cs`, và case dispatch trong `src/Forge.Plugin/PluginCommandProcessor.cs`. Tool không đăng ký sẽ bị fail closed như destructive.
- Ưu tiên typed tool hơn executor open-world; giữ nguyên đánh giá safety kép; thêm `SafetyPolicyTests` cho deny code mới.
- Không bao giờ commit token, DWG của khách hàng, hay binary Autodesk. File `*.log` sinh ra không đưa vào git.

## Báo cáo lỗ hổng

Báo riêng tư — đừng mở issue công khai cho phát hiện bảo mật. Ưu tiên: **GitHub Security Advisories** (mục "Report a vulnerability" của repo). Dự phòng: liên hệ qua profile tổ chức trên GitHub. Ghi rõ component bị ảnh hưởng (`Forge.Server`, `Forge.Plugin`, pipe protocol), bước tái hiện, mức độ tác động. Chúng tôi nhằm xác nhận trong 7 ngày — xin để khoảng thời gian hợp lý trước khi công khai.

Ghi chú trust boundary: pipe ACL giới hạn theo user hiện tại + LocalSystem và token chỉ là lớp phòng thủ bổ sung — giữ `FORGE_AUTOCAD_TOKEN` mạnh và duy nhất; `forge_exec_dotnet` là đường rủi ro nhất và luôn bị hai cổng.

## Versioning

Product SemVer **0.3.0** (git tag + GitHub Release) ≠ giai đoạn roadmap ≠ phiên bản MCP protocol ≠ năm AutoCAD target. Binary plugin không hoán đổi được giữa các năm AutoCAD lớn — build theo từng năm.

## Giấy phép

MIT — [LICENSE](LICENSE). Ghi nhận bên thứ ba: [NOTICE.md](NOTICE.md). API/DLL của Autodesk **không** được phát tán lại.
