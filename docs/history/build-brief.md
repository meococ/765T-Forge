# 765T-Forge — Build Brief

**MCP Server điều khiển AutoCAD (full-feature) cho AI Agent**
Thương hiệu: 765T · Bộ ba: **Flow** (Revit) · **Fly** (Navisworks) · **Forge** (AutoCAD)
Người review: Huỳnh Trung Trực (Mèo Cọc) · Ngày lập: 08/07/2026 · Version: 0.2 (đã chốt)

> **Disclaimer (publish):** Tài liệu này là **catalog / quyết định lịch sử** (aspirational). Bề mặt đã ship = [`../capability-matrix.md`](../capability-matrix.md) + SemVer trong `CHANGELOG.md`. Không dùng §5 như checklist “đã implement”. Định vị công khai: **reliable issue-set publish + agent safety**, không đua số tool.

> Tài liệu này để anh **review** rồi **dán thẳng cho Codex** lên plan & build. Các quyết định lớn **đã chốt**: toàn bộ stack **all-C#**, **AutoCAD full**, fork base cố định.

---

## 0. TL;DR — Bảng quyết định nhanh

| Hạng mục | Quyết định | Ghi chú |
|---|---|---|
| Mục tiêu | MCP điều khiển **full AutoCAD**, tập trung **xuất bản vẽ (drawing production)** công trình metro | AI agent thực thi *sau khi* anh gom đủ context/data |
| Triết lý phủ tính năng | **Không cắt** — dùng lớp *generic executor* để đạt 100% coverage | Đúng với MCP best-practice: "khi phân vân, ưu tiên phủ API toàn diện" |
| Kiến trúc | **Mode B**: C# .NET plugin (in-process) ↔ socket ↔ MCP server | Server drive plugin qua stdio/socket |
| Ngôn ngữ | **All-C#** (server + plugin) — một toolchain duy nhất | Server viết mới bằng C# SDK `ModelContextProtocol`. Forge **đứng độc lập, KHÔNG dùng chung code với Flow** |
| Fork base | Lấy **plugin C#** từ `moisesbritez92/autocad-2026` (+ `puran-water`, `ranvirw18`); **server viết mới bằng C#** | Bỏ tầng TypeScript của moisesbritez — xem mục 2 |
| Transport | **stdio** (server chạy local, drive AutoCAD local) | Không cần HTTP/remote ở giai đoạn này |
| Thứ tự build | **Hard-first**: xây nền (dispatcher + executor + safety) trước, structured tools sau | Xem lộ trình mục 7 |
| Ràng buộc sống còn | **Lớp an toàn bắt buộc** trước khi cho agent chạy lệnh tùy ý | Xem mục 6 |

---

## 1. Mục tiêu & phạm vi

**Bài toán thực tế:** Anh phụ trách phần hỗ trợ CAD để **xuất hồ sơ bản vẽ** cho công trình metro. Quy trình: anh thu thập đủ context + data (mã hiệu, layout, xref, tiêu chuẩn khung tên...) → giao AI agent (Forge) → agent **thực thi nhanh nhất, chính xác nhất, tự verify lại**.

**Phạm vi phiên bản này:** Full AutoCAD (bản đầy đủ, **không phải LT**), trọng tâm nghiệp vụ xuất bản vẽ metro. Civil 3D **không** nằm trong scope (đã có người khác phụ trách; để mở rộng sau nếu cần — kiến trúc bên dưới cho phép cắm thêm).

**Nguyên tắc "full features, không cắt" — được best-practice hậu thuẫn:** Tài liệu MCP chính thống khuyến nghị cân bằng giữa *workflow tools* (tiện cho tác vụ cụ thể) và *phủ API toàn diện* (cho agent linh hoạt tự tổ hợp), và **khi phân vân thì ưu tiên phủ toàn diện**. Cách hiện thực điều này mà không phải viết tay hàng nghìn command (bất khả thi) là **lớp generic executor** ở mục 4.

---

## 2. Quyết định fork-base & ngôn ngữ (ĐÃ CHỐT)

Ở lượt trước em gọi `puran-water/autocad-mcp` là repo "chỉn chu nhất" — điều đó vẫn đúng **về chất lượng code & pattern**, nhưng nó được xây cho **AutoCAD LT + AutoLISP** (dùng File IPC bắn phím vì LT không nạp được .NET plugin). Với mục tiêu **full AutoCAD + stack C# (mode B)** của anh, base khớp hơn là repo khác. Đây là điểm em phải nói thẳng thay vì fork bừa theo lời khen cũ.

| Tiêu chí | `moisesbritez92/autocad-2026` ⭐ | `puran-water/autocad-mcp` | `ranvirw18/autocad-mcp-server` |
|---|---|---|---|
| Kiến trúc điều khiển | **C# .NET plugin (live) + accoreconsole (headless)** | AutoLISP + File IPC (bắn phím) + ezdxf | COM automation (pyautocad) |
| Hợp mode B của anh | ✅ Đúng chuẩn | ❌ Không (LISP-centric) | ⚠️ COM, không phải plugin |
| Hỗ trợ full AutoCAD | ✅ AutoCAD 2026 full | ⚠️ Thiết kế cho LT 2024+ | ✅ Full (qua COM) |
| Ngôn ngữ server | Node.js/TypeScript | Python | Python (FastMCP) |
| Điểm mạnh để "cướp" | Xương sống plugin + headless + token auth | `execute_lisp` escape-hatch, ezdxf headless, IPC không cướp focus, taxonomy 8 nhóm tool, companion agent skill | Xuất PDF gọn, type hint + unit test, FastMCP sạch |

**Chiến lược đã chốt — "server C# viết mới + ghép plugin", toàn bộ all-C#:**

1. **Server MCP — viết mới bằng C#** dùng SDK chính thức `ModelContextProtocol` (Anthropic + Microsoft đồng phát triển). C# là thế mạnh của anh (Flow ~75% C#) và đã có MCP SDK C# sạch, nên không cần đụng TypeScript/Python. Forge **đứng độc lập** — không dùng chung code với Flow.
2. **Plugin AutoCAD — lấy từ `moisesbritez92/autocad-2026`:** plugin C# đã wire sẵn NETLOAD + command dispatch + accoreconsole cho AutoCAD 2026 full. **Bỏ tầng server TypeScript của nó**, chỉ giữ plugin C# rồi mở rộng.
3. **Ghép từ `puran-water`:** triết lý `execute_lisp` (chìa khóa "full features"), cách phân nhóm tool, và bài học **UTF-8** (khung tên metro tiếng Việt). *(Lưu ý: backend ezdxf của repo này là Python — mình **KHÔNG** port; nếu cần sinh DXF thuần code thì dùng thư viện C# như `netDxf`, xem mục 3.)*
4. **Ghép từ `ranvirw18`:** logic **xuất PDF/plot** + pattern schema/validation (port sang C#).

> ✅ **Đã chốt:** stack **all-C#** (server + plugin), một toolchain duy nhất. Transport **stdio**. AutoCAD **full** (mode B chạy được vì LT không nạp .NET plugin). Không ràng buộc code với Flow.

---

## 3. Kiến trúc tổng thể

Ba tầng, tách bạch rõ. Phần "hard" cần build trước nằm ở tầng plugin + transport.

```
┌─────────────────────────────────────────────────┐
│  AI Agent (Claude / Codex / Cursor...)           │
└───────────────────────┬─────────────────────────┘
                        │ MCP protocol (stdio, JSON-RPC)
┌───────────────────────▼─────────────────────────┐
│  765T-Forge MCP Server  (C#, MCP C# SDK)         │
│  ├─ Tool registry (structured + generic)         │
│  ├─ Input schema validate (C# → JSON)            │
│  ├─ Safety layer (denylist, dry-run, audit)      │
│  └─ Dispatcher → forward JSON command             │
└───────────────────────┬─────────────────────────┘
        ┌───────────────┴────────────────┐
        │ (live: socket/named-pipe)      │ (headless: spawn process)
┌───────▼────────────────┐   ┌───────────▼──────────────────┐
│  C# .NET Plugin         │   │  accoreconsole.exe            │
│  (NETLOAD trong AutoCAD)│   │  chạy .scr/.lsp trên .dwg     │
│  ├─ Socket listener     │   │  (batch, không mở AutoCAD)    │
│  ├─ Command dispatcher  │   └───────────────────────────────┘
│  ├─ Transaction wrapper │   ┌──────────────────────────────┐
│  └─ Full .NET/ObjectARX │   │  netDxf (C#, tùy chọn)        │
│     API surface         │   │  sinh/đọc DXF thuần C#         │
└───────┬────────────────┘   └──────────────────────────────┘
        │
┌───────▼─────────────────────────────────────────┐
│  AutoCAD Full (Autodesk.AutoCAD.* .NET API)      │
│  Database · Editor · Plot · Publisher · Xref...  │
└──────────────────────────────────────────────────┘
```

**Vì sao mode B (plugin) chứ không phải COM thuần:** COM (pyautocad) chỉ chạm được tập con hạn chế và dễ gây mất ổn định; plugin C# chạy in-process cho anh **toàn bộ** bề mặt .NET/ObjectARX (Plot API, Publisher, LayerState, Field, dynamic block...) — thứ COM không với tới. Đúng tinh thần "build hard, full features".

**Ba đường điều khiển bổ trợ nhau:**
- **Live plugin (socket):** tương tác thời gian thực với bản vẽ đang mở — đường chính.
- **accoreconsole (headless):** batch hàng loạt .dwg không cần mở AutoCAD — cho QA/publish số lượng lớn.
- **netDxf (tùy chọn, C#):** thư viện DXF thuần C#, sinh/đọc DXF không cần AutoCAD — cho tác vụ offline nhẹ.

---

## 4. Chiến lược "full features không cắt" — 2 lớp tool

Đây là trái tim của triết lý "no cut". Không ai wrap tay được hàng nghìn command AutoCAD; cũng không nên. Thay vào đó dùng **2 lớp**:

### Lớp A — Structured tools (typed, an toàn, cho 80% tác vụ thường gặp)
Wrapper có validate schema, có annotation, có verify. An toàn để agent gọi tự do. Danh mục đầy đủ ở mục 5.

### Lớp B — Generic executors (chìa khóa đạt 100% coverage)
Bốn "cửa thoát hiểm" cho phần đuôi dài — bất kỳ chức năng nào AutoCAD có mà chưa được wrap:

| Tool | Chức năng | Coverage | Ghi chú an toàn |
|---|---|---|---|
| `forge_exec_command` | Gửi **bất kỳ command string** AutoCAD nào | ~95% (mọi thứ gõ được ở dòng lệnh) | ⚠️ destructive — qua denylist |
| `forge_exec_lisp` | Chạy **bất kỳ biểu thức AutoLISP** | Toàn bộ AutoLISP/Visual LISP (lấy từ puran-water) | ⚠️ destructive — qua denylist |
| `forge_exec_dotnet` | Chạy **snippet C#/.NET** trong context plugin (Roslyn) → chạm ObjectARX/.NET API mà LISP không với tới | 100% — phần .NET-only | 🔴 nguy hiểm nhất — mặc định **tắt**, bật qua config |
| `forge_run_script` | Chạy file `.scr` (batch, headless qua accoreconsole) | Batch automation | ⚠️ auto-backup trước khi chạy |

> **Kết quả:** structured tools lo phần thường xuyên (nhanh + an toàn + agent dễ dùng đúng); generic executors đảm bảo **không tính năng nào bị cắt**. `forge_exec_lisp` một mình đã phủ gần hết; `forge_exec_dotnet` bịt nốt phần .NET-only.

**MCP annotations (khai báo cho mọi tool):** mỗi tool phải gắn `readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint` theo chuẩn MCP. Đây vừa là best-practice, vừa là **nền cho lớp an toàn** (mục 6) — agent và server đều biết tool nào chỉ đọc, tool nào phá hủy.

**Quy ước đặt tên:** `forge_<nhóm>_<hành động>` (vd `forge_layer_create`, `forge_plot_publish`, `forge_exec_command`). Prefix nhất quán + động từ rõ ràng giúp agent chọn đúng tool.

---

## 5. Danh mục tool đầy đủ (full coverage)

12 nhóm. Cột **Hot** đánh dấu đường xương sống của nghiệp vụ xuất bản vẽ metro (làm trước ở Phase 1, **không** loại bỏ nhóm nào khác). Cột **Ann** = annotation gợi ý (R=readOnly, D=destructive, I=idempotent).

### 5.1 `system` — điều khiển phiên & tiện ích nền
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_system_connect` / `_health` / `_version` | Handshake, kiểm tra kết nối, phiên bản AutoCAD | ● | R |
| `forge_system_getvar` / `_setvar` | Đọc/ghi system variable (FILEDIA, PSTYLEMODE, PDMODE...) | ● | R / D-I |
| `forge_system_undo` / `_redo` | Undo/redo một bước (lấy từ puran-water) | | D |
| `forge_system_transaction` | Bọc nhiều thao tác trong 1 transaction (commit/abort) | | — |
| `forge_system_audit` / `_purge` | AUDIT sửa lỗi DB, PURGE dọn rác | | D |

### 5.2 `document` — quản lý bản vẽ & không gian
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_doc_new` / `_open` / `_save` / `_saveas` / `_close` | Vòng đời file (mở với FILEDIA=0) | ● | D / D / R-I |
| `forge_doc_list_open` | Liệt kê document đang mở | | R |
| `forge_doc_list_layouts` | Liệt kê toàn bộ layout | ● | R |
| `forge_doc_switch_layout` / `_switch_space` | Chuyển layout / model⇄paper space | ● | I |

### 5.3 `layer` — layer & layer state
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_layer_list` / `_create` / `_set_current` / `_delete` | CRUD layer | ● | R / D |
| `forge_layer_set_props` | Color, linetype, lineweight, plot/no-plot | ● | D-I |
| `forge_layer_freeze` / `_thaw` / `_on` / `_off` / `_lock` / `_unlock` | Trạng thái hiển thị/khóa | ● | I |
| `forge_layer_state_save` / `_restore` / `_list` | **Layer State Manager** (quan trọng cho publish) | ● | R / I |
| `forge_layer_vp_freeze` | Freeze layer trong 1 viewport cụ thể | ● | I |

### 5.4 `entity` — thực thể hình học
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_entity_query` | Liệt kê/lọc entity theo type/layer/vùng chọn | | R |
| `forge_entity_create_*` | line, polyline, circle, arc, ellipse, spline, hatch, region, 3D solid, mesh... | | D |
| `forge_entity_modify_*` | move, copy, rotate, scale, mirror, offset, trim, extend, fillet, chamfer, array, explode, join | | D |
| `forge_entity_delete` | Xóa entity (theo id/selection) | | 🔴 D |
| `forge_entity_get_props` / `_set_props` | Đọc/ghi thuộc tính entity | | R / D-I |
| `forge_entity_selection_set` | Tạo/thao tác selection set (giống Navisworks anh quen) | | R |

### 5.5 `block` — block & attribute
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_block_list` / `_insert` / `_define` / `_redefine` | Quản lý block definition & reference | ● | R / D |
| `forge_block_list_attributes` | Liệt kê attribute của block ref | ● | R |
| `forge_block_get_attr` / `_set_attr` | **Đọc/ghi giá trị attribute** (lõi điền khung tên) | ● | R / D-I |
| `forge_block_wblock` / `_explode` | WBLOCK ra file / explode | | D |
| `forge_block_dynamic_props` | Đọc/ghi tham số dynamic block | | R / D-I |

### 5.6 `annotation` — chú thích
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_anno_text` / `_mtext` | Text / MText | ● | D |
| `forge_anno_dimension` | Mọi kiểu dim (linear, aligned, angular, radial, ordinate...) | | D |
| `forge_anno_leader` / `_mleader` | Leader / multileader | | D |
| `forge_anno_table` | Bảng (table) — thống kê, bảng kê | ● | D |
| `forge_anno_field` | **Field liên kết** (số tờ/tổng số tờ tự cập nhật) | ● | D-I |
| `forge_anno_hatch` / `_revcloud` / `_wipeout` | Hatch/gradient, revision cloud, wipeout | | D |
| `forge_anno_scale` | Quản lý annotation scale | | I |

### 5.7 `xref` — external reference (metro dùng cực nhiều)
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_xref_attach` / `_detach` / `_reload` / `_unload` / `_bind` | Vòng đời xref | ● | D / I |
| `forge_xref_list` | Liệt kê xref + trạng thái + đường dẫn | ● | R |
| `forge_xref_repath` | **Sửa path (relative/absolute)** — chống mất nét khi plot | ● | D-I |
| `forge_xref_clip` | Xref clip boundary | | D |

### 5.8 `layout` / `view` — bố cục & khung nhìn
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_layout_create` / `_import` / `_delete` / `_rename` | Layout (import từ DWT mẫu công ty) | ● | D |
| `forge_view_viewport_create` / `_set_scale` / `_lock` | Viewport + tỉ lệ + khóa | ● | D-I |
| `forge_view_named` | Named view (tạo/gọi) | | R / I |
| `forge_view_zoom` / `_pan` | Zoom/pan (Extents, Window, object) | | I |
| `forge_layout_page_setup_create` / `_import` / `_apply` | **Page Setup** (import setup chuẩn thay vì set tay) | ● | D-I |

### 5.9 `plot` / `publish` — xuất bản (trái tim nghiệp vụ)
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_plot_to_pdf` | Plot 1 layout ra PDF | ● | D-I |
| `forge_plot_publish` | **Batch publish** nhiều layout → 1 PDF gộp *hoặc* nhiều PDF (qua DSD) | ● | D-I |
| `forge_plot_set_style` | Gán plot style CTB/STB | ● | D-I |
| `forge_plot_config` | Device, khổ giấy, tỉ lệ, hướng, plot area | ● | I |
| `forge_plot_preview` | Preview trước khi in | | R |
| `forge_plot_to_dwf` | Xuất DWF (nếu cần) | | D-I |

### 5.10 `data` / `extract` — trích xuất & lập chỉ mục
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_data_extract` | Data Extraction: attribute → table/CSV | | R |
| `forge_data_block_count` / `_layer_report` | Thống kê block / báo cáo layer | | R |
| `forge_data_export_dxf` | Xuất DXF (dùng netDxf, C#) | | R |
| `forge_data_index_sqlite` | Lập chỉ mục element vào SQLite để query (lấy từ puran-water/zh19980811) | | I |

### 5.11 `qa` / `verify` — kiểm tra chất lượng (rất hợp gu anh)
| Tool | Mô tả | Hot | Ann |
|---|---|---|---|
| `forge_qa_verify_titleblock` | **Đọc lại attribute khung tên, so với data nguồn** → báo sai lệch | ● | R |
| `forge_qa_check_xrefs` | Phát hiện xref thiếu/sai path trước khi plot | ● | R |
| `forge_qa_audit_layers` | Đối chiếu layer với chuẩn công ty | ● | R |
| `forge_qa_standards_check` | Kiểm tra drawing standards (nếu có .dws) | | R |
| `forge_qa_readback` | Wrapper verify chung: sau mọi thao tác ghi → đọc lại xác nhận | ● | R |

### 5.12 `exec` — generic executors (xem mục 4)
`forge_exec_command` · `forge_exec_lisp` · `forge_exec_dotnet` · `forge_run_script`

> **Cách "update thêm tool call":** khi gặp nhu cầu mới, agent dùng ngay `forge_exec_lisp`/`forge_exec_command` để làm được việc; nếu tác vụ đó lặp lại thường xuyên thì "chính thức hóa" nó thành một structured tool mới (wrapper có validate + verify). Vòng lặp này giúp Forge **lớn dần** mà không bao giờ bị "cắt tính năng".

---

## 6. 🔴 Lớp an toàn (BẮT BUỘC — vì AI chạy lệnh tùy ý)

Đây là ràng buộc **không thương lượng**. Cho một AI agent quyền chạy `forge_exec_command`/`_lisp`/`_dotnet` là cực mạnh nhưng cũng cực rủi ro: một lệnh sai (`ERASE all`, `PURGE`, `SAVEAS` đè file, `-OVERKILL`) có thể phá hồ sơ. Phải xây lớp này **cùng lúc với** nền, trước khi agent được drive.

1. **Denylist lệnh phá hủy** — chặn/bắt xác nhận với: `ERASE`+`all`, `PURGE`/`-PURGE`, `OVERKILL`, `WBLOCK` (đè), `SAVEAS`/`QSAVE` (đè file khác), `RECOVER`, `AUDIT` với fix, mọi lệnh chứa `all`/`*`. Match cả trong `forge_exec_command`/`_lisp`.
2. **Auto-backup trước mọi thao tác ghi/batch** — copy `.dwg` sang thư mục backup kèm timestamp trước khi chạy publish/exec. Rẻ mà cứu mạng.
3. **Transaction wrapping + undo mỗi tool call** — mỗi tool ghi bọc trong 1 transaction; lỗi → abort; thành công → cho phép undo một bước.
4. **Dry-run / preview mode** — batch op có cờ `dry_run=true` để agent xem "sẽ làm gì" trước khi thực thi thật.
5. **Read-back verification** (`forge_qa_readback`) — sau mọi thao tác ghi, đọc lại kết quả xác nhận. Đây là pattern QA anh vốn dùng (kiểu bắt lỗi "Cầu Vân Chối/Vạn Củi").
6. **`forge_exec_dotnet` mặc định TẮT** — chỉ bật qua flag `enable_unsafe_ops: true` trong config, có cảnh báo rõ.
7. **Audit log** — ghi lại *mọi* command đã chạy (timestamp, tool, tham số, kết quth) ra file log để truy vết.
8. **Error message actionable** — lỗi phải gợi ý bước sửa cụ thể ("Xref X mất path, gọi `forge_xref_repath` với đường dẫn Y"), không chỉ báo "failed".

---

## 7. Lộ trình build "hard-first"

Tinh thần của anh: **làm phần khó trước, sau sẽ dễ.** Điều này khớp hoàn hảo với kiến trúc escape-hatch — khi nền (dispatcher + socket + generic executor + safety) đã xong, thêm mỗi structured tool chỉ là wrapper mỏng ⇒ dễ.

### Phase 0 — NỀN (phần khó, ~60% công sức, LÀM TRƯỚC)
1. Lấy **plugin C#** từ `moisesbritez92/autocad-2026` (bỏ tầng server TypeScript); dựng **server C# mới** (SDK `ModelContextProtocol`); build + kết nối được (plugin NETLOAD ↔ server handshake).
2. Củng cố **transport**: socket/named-pipe server↔plugin, connect/reconnect bền, giữ token auth có sẵn.
3. Xây **command dispatcher** trong plugin C#: route JSON command → gọi .NET API, trả structured error.
4. Hiện thực **4 generic executor** (`exec_command`, `exec_lisp`, `run_script`/accoreconsole, `exec_dotnet` qua Roslyn — cái cuối làm sau cùng trong phase này). ⇒ **Ngay sau bước này agent đã có full control AutoCAD.**
5. Xây **lớp an toàn** (mục 6) — song song, không được bỏ.
6. `forge_qa_readback` + audit log.
> ✅ Kết thúc Phase 0: **full AutoCAD control + an toàn**. Mọi thứ sau đây là "easy".

### Phase 1 — Structured tool library (dễ, đắp lên nền)
Hiện thực nhóm 5.1–5.9, **ưu tiên đường Hot (●)** để chạy trọn vòng nghiệp vụ metro trước: `doc_list_layouts` → `xref_reload`+`repath` → `layer_state_restore` → `block_set_attr` (điền khung) → `page_setup_import` → `plot_publish` → `qa_verify_titleblock`. Mỗi tool = wrapper typed + verify quanh thứ executor đã làm được.

### Phase 2 — Headless & batch
Nối `accoreconsole` cho batch publish/QA hàng loạt .dwg không mở AutoCAD; tùy chọn thêm thư viện **netDxf** (C#) cho sinh/đọc DXF offline.

### Phase 3 — Data/QA & hoàn thiện
Nhóm 5.10–5.11 (data extraction, SQLite index, bộ verify khung tên); viết **companion agent skill** (một SKILL.md để agent biết cách dùng Forge cho đúng); docs + test.

### Phase 4 — Evaluations
Theo MCP best-practice: soạn ~10 câu hỏi test (độc lập, read-only, đủ phức tạp, kết quả kiểm chứng được) để đo agent có dùng Forge hiệu quả không. Test bằng **MCP Inspector** (`npx @modelcontextprotocol/inspector`).

---

## 8. Gotchas kỹ thuật (báo trước để Codex khỏi vấp)

- **Phiên bản .NET runtime phải khớp:** AutoCAD 2026 chạy trên **.NET 8**; plugin phải target đúng, DLL tham chiếu `AcMgd.dll` / `AcCoreMgd.dll` / `AcDbMgd.dll`. Sai runtime → không NETLOAD được.
- **NETLOAD trust/security:** đặt DLL vào **trusted location** (biến `TRUSTEDPATHS`) nếu không AutoCAD chặn nạp.
- **Xref path relative + reload:** metro xref chằng chịt — attach bằng path **relative** và luôn `reload` trước khi plot, không thì PDF thiếu nét.
- **CTB vs STB:** một file chỉ theo **một** loại plot style. Đọc `PSTYLEMODE` trước, đừng gán bừa → sai màu/độ dày nét.
- **Suppress dialog khi batch:** set `FILEDIA=0`, `BACKGROUNDPLOT=0`, `CMDDIA` phù hợp, không thì automation treo chờ hộp thoại.
- **Field cho số tờ:** dùng `forge_anno_field` liên kết để số tờ/tổng số tờ tự cập nhật, tránh lệch thủ công khi thêm/bớt bản vẽ.
- **COM SendCommand bất đồng bộ (nếu có dùng):** lệnh chạy async, phải chờ hoàn tất (`command_delay`) trước lệnh kế — nguyên nhân crash kinh điển.
- **Publish qua DSD:** batch publish nhiều layout cần dựng đúng file **DSD** (Drawing Set Description); tham khảo cấu trúc DsdData/DsdEntry trong .NET API.
- **Unicode tiếng Việt:** khung tên metro có tiếng Việt → xử lý **UTF-8** cẩn thận ở cả IPC lẫn ghi attribute (puran-water có note "UTF-8 fallback" — học theo).
- **Locked layer / VP freeze:** thao tác entity trên layer khóa hoặc bị VP-freeze sẽ âm thầm thất bại — kiểm tra trạng thái layer trước khi ghi.
- **Multi-document context:** chú ý "active document" khi mở nhiều bản vẽ — thao tác nhầm document là lỗi khó soi.

---

## 9. Checklist bàn giao cho Codex

Đoạn để anh dán kèm khi giao Codex:

```
NHIỆM VỤ: Fork & build "765T-Forge" — MCP server điều khiển AutoCAD full-feature,
phục vụ AI agent xuất hồ sơ bản vẽ công trình metro.

STACK: ALL-C#. Server MCP viết mới bằng C# (SDK ModelContextProtocol chính thức).
PLUGIN: lấy plugin C# từ github.com/moisesbritez92/autocad-2026 — BỎ tầng server TS
của nó, giữ + mở rộng plugin C# + accoreconsole. Forge độc lập, KHÔNG dùng chung
code với Flow. AutoCAD FULL (không phải LT — LT không nạp .NET plugin).
Port ý tưởng: puran-water/autocad-mcp (execute_lisp, UTF-8); ranvirw18 (plot/PDF).
DXF thuần code (nếu cần): thư viện C# netDxf — KHÔNG dùng ezdxf/Python.

RÀNG BUỘC CỨNG:
1. Kiến trúc mode B, ALL-C#: C# plugin in-process (full .NET/ObjectARX API) ↔ stdio C# MCP server.
2. Phủ 100% tính năng qua 4 generic executor: exec_command, exec_lisp, run_script,
   exec_dotnet (Roslyn, mặc định TẮT). KHÔNG cắt tính năng.
3. Lớp an toàn BẮT BUỘC trước khi agent chạy: denylist lệnh phá hủy, auto-backup .dwg,
   transaction+undo, dry-run, read-back verify, audit log, gate exec_dotnet.
4. Mọi tool khai báo MCP annotation (readOnlyHint/destructiveHint/idempotentHint).
5. Đặt tên tool: forge_<nhóm>_<hành động>.

THỨ TỰ BUILD (hard-first):
Phase 0 nền (transport + dispatcher + 4 executor + safety) → Phase 1 structured tools
(ưu tiên đường Hot: layout/xref/layerstate/block-attr/pagesetup/publish/verify) →
Phase 2 headless batch → Phase 3 data/QA + companion SKILL.md → Phase 4 evals.

SPEC TOOL ĐẦY ĐỦ: xem mục 5 của build brief (12 nhóm).
GOTCHAS: xem mục 8 (đặc biệt: .NET 8 runtime, xref relative+reload, CTB/STB, FILEDIA=0,
Field số tờ, UTF-8 tiếng Việt).
TEST: MCP Inspector.
```

---

*Hết build brief. Mọi quyết định lớn đã chốt (all-C#, AutoCAD full, fork base cố định) — sẵn sàng giao Codex. Sau khi anh review, em có thể: (1) sinh khung code Phase 0 mẫu bằng C#, (2) viết chi tiết schema cho từng tool nhóm Hot, hoặc (3) soạn companion SKILL.md cho agent.*
