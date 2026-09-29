# Raccoon Build Editor (`com.raccoon.build-editor`)

Editor tool build Android (APK/AAB) cho **Unity 6** (6000.0+). Backend luôn IL2CPP, compression luôn LZ4HC.

## Cài đặt

Source nằm ở `unity_build_editor/Assets/UnityBuilder/`; GitHub Action tách nó ra branch `upm` + tag `v<version>`.

Package Manager → `+` → **Add package from git URL...**:

```
https://github.com/namtuoc91/unity_build_editor.git#v0.0.9
```

Hoặc bản mới nhất: `https://github.com/namtuoc91/unity_build_editor.git#upm`

Hoặc thêm thẳng vào `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.raccoon.build-editor": "https://github.com/namtuoc91/unity_build_editor.git#v0.0.9"
  }
}
```

## Sử dụng
Menu **Tools → Raccoon → Android Build**.

| Phần | Nội dung |
|---|---|
| Preset bar | Chọn / thêm / duplicate / xóa preset (mặc định Dev + Release). Đổi tên ở mục App. |
| App | App Name + Icon (kéo thả texture → set Default Icon + clear icon Android Adaptive/Round/Legacy), App ID (theo preset), Version, Version Code (−/+), Auto-increment |
| Build | Mode, Development Build (+ Script Debugging / Profiler), No Ads, No Ads Rules (foldout), Use Test Ad, Clean cache, App Bundle size warning |
| Signing | Keystore path + alias (lưu config), password (chỉ SessionState) |
| Scenes | Danh sách scene build, đồng bộ với Build Settings |
| Output | Folder + template tên file |
| Define Symbols | Read-only, symbol hiện có của Android |
| AdMob Mediation | Read-only: network, version package, adapter, đối chiếu `mainTemplate.gradle`, Refresh / Force Resolve |
| Devices / History APK / History AAB | adb device list; lịch sử build tách theo APK và AAB (mở folder / install lại (APK) / xóa) |

### Mode

| Mode | Format | Architecture | Ghi chú |
|---|---|---|---|
| Dev | APK | ARM64 | Build & Install qua adb; keystore thiếu → debug keystore |
| Release | AAB | ARMv7 + ARM64 | Bắt buộc keystore; ép tắt Development Build / No Ads / Test Ad |

### Ads (`com.raccoon.adpack`, nếu có cài)
- Dev + No Ads → `_creativeMode = true` (không đụng `use_test_ad`).
- Dev + có Ads → `_creativeMode = false`, `use_test_ad` = checkbox Use Test Ad.
- Release → `_creativeMode = false` + `use_test_ad = false`.
- Tool **sửa thật + save** scene trong build list và asset `AdsScriptableObj`, không restore sau build.

### No Ads Rules (riêng từng project)
Dùng khi game có object cần đổi thêm biến theo No Ads (vd ẩn nút Remove Ads, tắt banner placeholder). Nằm dưới checkbox No Ads (mục Build), luôn hiện và thu gọn/mở được; tiêu đề ghi đang áp Value No Ads hay Value Has Ads. Lưu trong `RaccoonBuildConfig.json` (dùng chung mọi preset) nên set 1 lần, lần sau không phải set lại.

| Field | Ý nghĩa |
|---|---|
| Scene | Chọn scene trong project (kéo thả / picker / ▼ chia nhóm Build list · Khác); trống = mọi scene trong build list |
| Object path | Đường dẫn hierarchy từ root, vd `Canvas/Shop/BtnRemoveAds` (trùng tên → áp cho tất cả). ▼ = chọn theo hierarchy của scene đã chọn |
| Component | Tên type ngắn/full; rỗng hoặc `GameObject` = chính GameObject (vd property `m_IsActive`) |
| Property | SerializedProperty path (`_hideRemoveAds`, `m_IsActive`…), chỉ bool / int / float / string / enum |
| Value No Ads / Value Has Ads | Giá trị ghi vào. Chọn Property bằng ▼ → ô nhập theo kiểu: bool = dropdown true/false, enum = dropdown tên, int/float = checkbox "Đổi" + ô số, string = ô text. "(không đụng)" / bỏ tick / rỗng = không đụng. Gõ Property tay → ô text (`true`/`false`, số dấu chấm, tên enum) |

- Kéo GameObject từ Hierarchy (hoặc Component từ header Inspector) vào ô cạnh "Rule n" để tự điền; nút ▼ chọn object / component / property — scene chưa mở thì tool mở tạm (Additive) để đọc rồi đóng.
- "Value Has Ads" áp cho Dev không bật No Ads **và Release** → nên điền cả 2 giá trị để build Release luôn đúng.
- Build: mở từng scene → set qua `SerializedObject` → **save scene** (không restore), giống `_creativeMode`. Không tìm thấy object / component / property, parse sai giá trị hoặc đọc lại sai → **chặn build**.
- Nút "Kiểm tra (No Ads / Has Ads)": chạy thử, báo giá trị hiện tại và giá trị sẽ set, không sửa gì.
- Nút "Áp dụng + Save (No Ads / Has Ads)": set + save scene thật như lúc build nhưng **không build** → mở scene kiểm tra rồi mới Build.
- Chưa cài adpack mà có rule → vẫn bật được No Ads (chỉ áp rule).

### Version code
`current = max(config, PlayerSettings)`. Auto-increment → build với `current + 1`; build thành công mới lưu vào config, fail/cancel thì trả PlayerSettings về `current`.

### File dữ liệu
- `ProjectSettings/RaccoonBuildConfig.json`: config + preset (commit được, không chứa password).
- `Library/RaccoonBuildHistory.json`: history local; giữ 20 APK / 10 AAB gần nhất, bản cũ hơn bị xóa cả file.

### Tên file
Token: `{product} {version} {code} {date} {time} {mode} {noads}` (`{noads}` → `_noads` khi bật No Ads).

## Chạy test
Trong repo này: **Window → General → Test Runner → EditMode** (assembly `Raccoon.BuildEditor.Editor.Tests`).

## Ghi chú
- Đổi keystore giữa 2 bản build thì `adb install -r` sẽ lỗi signature → phải uninstall app trước.
- Popup "App Bundle size" của Unity: set ngưỡng ở mục Build (mặc định 210 MB, 0 = tắt).
