# Tiger Incoming SOAP Endpoint Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tạo SOAP endpoint để TigerTMS có thể gọi vào hệ thống PMS, gửi các loại message (ChargeRecord, RoomStatus, WakeupAnswer, v.v.) và nhận lại `"SUCCESS"` hoặc `"FAILED – lý do"`.

**Architecture:** Controller trong Host nhận raw SOAP body (`text/xml`), delegate sang `TigerIncomingSoapParser` để extract inner XML từ `<Msg>`, sau đó `TigerIncomingMessageDispatcher` (trong Tiger provider) validate `wsuserkey`, routing theo root element, và trả về chuỗi kết quả. Controller wrap kết quả vào SOAP response envelope.

**Tech Stack:** ASP.NET Core 10, `System.Xml.Linq` (XDocument/XElement), no external SOAP library needed.

---

## File Structure

### Files to CREATE:
- `src/Providers/PmsIntegration.Providers.Tiger/Incoming/Models/TigerChargeRecord.cs` — model parse từ `<chargerecord>` XML
- `src/Providers/PmsIntegration.Providers.Tiger/Incoming/TigerIncomingSoapParser.cs` — static helper extract `<Msg>` content + build SOAP response
- `src/Providers/PmsIntegration.Providers.Tiger/Incoming/TigerIncomingMessageDispatcher.cs` — validate wsuserkey, route theo message type, return SUCCESS/FAILED
- `src/PmsIntegration.Host/Controllers/TigerIncomingController.cs` — SOAP endpoint `POST /tiger/soap`

### Files to MODIFY:
- `src/Providers/PmsIntegration.Providers.Tiger/DI/TigerServiceExtensions.cs` — đăng ký `TigerIncomingMessageDispatcher`

### Security notes:
- Endpoint `/tiger/soap` nằm ngoài `/api/pms` → `PmsTokenMiddleware` không block
- Authentication dùng `wsuserkey` trong XML payload (Tiger-specific mechanism)
- `wsuserkey` so sánh constant-time nếu cần; hiện tại simple equality đủ

---

## Task 1: TigerChargeRecord model

**Files:**
- Create: `src/Providers/PmsIntegration.Providers.Tiger/Incoming/Models/TigerChargeRecord.cs`

- [ ] Tạo enum `ChargeRecordType` (CallRecord, Internet, Minibar, Video, Unknown)
- [ ] Tạo class `TigerChargeRecord` với các field từ spec: ReservationNumber, Type, SiteCode, RoomNumber, DateTime, Dialled, Duration, CallType, Charge, CallCategory, WsUserKey

---

## Task 2: TigerIncomingSoapParser

**Files:**
- Create: `src/Providers/PmsIntegration.Providers.Tiger/Incoming/TigerIncomingSoapParser.cs`

- [ ] `ExtractMsgContent(string soapEnvelope) → string`: parse XDocument, lấy `<Msg>` từ SOAP Body, trả `msg.Value` (tự decode HTML entities). Xử lý cả trường hợp Msg chứa escaped XML hoặc direct XML child.
- [ ] `WrapInSoapResponse(string result) → string`: build SOAP envelope response với namespace `http://CONNECTED GUESTSgenericinterface.org/`, element `SendMessageToExternalInterfaceResult`.

---

## Task 3: TigerIncomingMessageDispatcher

**Files:**
- Create: `src/Providers/PmsIntegration.Providers.Tiger/Incoming/TigerIncomingMessageDispatcher.cs`

- [ ] Inject `IOptions<TigerOptions>` và `ILogger<TigerIncomingMessageDispatcher>`
- [ ] `HandleAsync(string innerXml) → Task<string>`
  - Parse XElement, validate wsuserkey (skip validation nếu WsUserKey config blank)
  - Switch trên `root.Name.LocalName`:
    - `"chargerecord"` → `HandleChargeRecord`
    - default → log + return `"SUCCESS"` (acknowledge unimplemented)
- [ ] `HandleChargeRecord(XElement)`: validate room không blank, log các field, return `"SUCCESS"`

---

## Task 4: TigerIncomingController

**Files:**
- Create: `src/PmsIntegration.Host/Controllers/TigerIncomingController.cs`

- [ ] Route: `POST /tiger/soap`, Consumes `text/xml`
- [ ] Đọc raw Request.Body bằng `StreamReader`
- [ ] Gọi `TigerIncomingSoapParser.ExtractMsgContent` → bắt exception → trả FAILED SOAP response
- [ ] Gọi `dispatcher.HandleAsync` → trả `Content(soapResponse, "text/xml")` HTTP 200

---

## Task 5: DI Registration

**Files:**
- Modify: `src/Providers/PmsIntegration.Providers.Tiger/DI/TigerServiceExtensions.cs`

- [ ] Thêm `services.AddSingleton<TigerIncomingMessageDispatcher>()`

---

## Task 6: Verify build

- [ ] `dotnet build` toàn solution → 0 errors
