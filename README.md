# Raccoon Build Editor (`com.raccoon.build-editor`)

Editor tool build Android (APK/AAB) cho **Unity 6** (6000.0+). Backend luôn IL2CPP.

## Cài đặt

Source nằm ở `unity_build_editor/Assets/UnityBuilder/`; GitHub Action tách nó ra branch `upm` + tag `v<version>`.

Package Manager → `+` → **Add package from git URL...**:

```
https://github.com/namtuoc91/unity_build_editor.git#v0.0.3
```

Hoặc bản mới nhất: `https://github.com/namtuoc91/unity_build_editor.git#upm`

Hoặc thêm thẳng vào `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.raccoon.build-editor": "https://github.com/namtuoc91/unity_build_editor.git#v0.0.3"
  }
}
```

## Sử dụng
Menu **Tools → Raccoon → Android Build**.

| Phần | Nội dung |
|---|---|
| Preset bar | Chọn / thêm / duplicate / xóa preset (mặc định Dev + Release). Đổi tên ở mục App. |
| App | App Name + Icon (kéo thả texture → set Default Icon + clear icon Android Adaptive/Round/Legacy), App ID (theo preset), Version, Version Code (−/+), Auto-increment |
| Build | Mode, Development Build (+ Script Debugging / Profiler), No Ads, Use Test Ad, Clean cache, App Bundle size warning |
| Signing | Keystore path + alias (lưu config), password (chỉ SessionState) |
| Scenes | Danh sách scene build, đồng bộ với Build Settings |
| Output | Folder + template tên file |
| Define Symbols | Read-only, symbol hiện có của Android |
| AdMob Mediation | Read-only: network, version package, adapter, đối chiếu `mainTemplate.gradle`, Refresh / Force Resolve |
| Devices / History | adb device list; lịch sử build (mở folder / install lại / xóa) |

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
