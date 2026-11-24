# KittyDy0 Launcher

## Tao Setup.exe
Giai nen zip vao mot thu muc TRONG, roi nhap dup TAO-SETUP.bat.
Script tu build ban doc lap va tao Output\KittyDy0Launcher-Setup.exe (can Inno Setup).

## Chay thu nhanh (khong can Setup)
dotnet run

## Du lieu cua launcher (%AppData%\KittyDy0Launcher)
- instances\<id>\instance.json   cau hinh tung instance
- instances\<id>\minecraft\      saves, mods, resourcepacks, options... RIENG cua instance
- shared\                        libraries, assets, versions DUNG CHUNG (khong tai lai)
- java\                          Java do launcher tu tai (adoptium-17, azul-21, ...)
- config.json                    cai dat chung

## Font (tuy chon)
Bo file .ttf cua Bricolage Grotesque va Be Vietnam Pro vao thu muc Fonts.

## Nhap modpack (.mrpack / .zip)
Tab Instance -> "Nhap modpack (.mrpack / .zip)". Launcher nhan dien loai file theo noi dung:
- .mrpack (Modrinth): tao instance moi, tu cai Fabric/Quilt/Forge/NeoForge, tai mod (kiem tra SHA), chep overrides.
- .zip CurseForge (co manifest.json): tao instance moi, cai loader, chep overrides.
  Tai mod can API key: dien "CurseForgeApiKey" vao config.json (key mien phi tai console.curseforge.com).
- .zip thuong (khong manifest): giai nen vao instance dang chon (tu bo thu muc boc ngoai; zip chi co .jar thi vao mods).

## Dang nhap (tab Tai khoan + o "Tai khoan" o Trang chu)
- Offline: nhap ten 3-16 ky tu (chu, so, gach duoi).
- Microsoft/Xbox: CmlLib.Core.Auth.Microsoft, phien luu o %AppData%\KittyDy0Launcher\ms-accounts.json, co nut Dang xuat.
- Ely.by: dang nhap bang tai khoan Ely.by (ho tro 2FA). Mat khau khong duoc luu; token ma hoa DPAPI o ely-account.dat.
  Game chay kem authlib-injector (tu tai ve cache lan dau): -javaagent:authlib-injector.jar=ely.by

## Mod loader (tab Instance -> Mod loader)
- Chon loai (Fabric, Quilt, Forge, NeoForge) va phien ban (mac dinh "Moi nhat"), bam "Tai & cai mod loader".
- Danh sach ban lay theo phien ban Minecraft cua instance; doi phien ban Minecraft thi loader cu bi go (vi khong con khop).
- Fabric/Quilt khong can Java; Forge/NeoForge tu tai Java phu hop neu thieu, roi chay installer.
- "Go mod loader" chi bo lien ket, khong xoa file loader va thu muc mods.

## Liquid Glass (Cai dat -> Giao dien)
- Cong tac "Liquid Glass" ON: nen kinh nhieu lop, vien phan chieu, inner highlight, bong nhieu lop, glow khi hover, anh sang chuyen dong nhe.
  OFF: Dark Flat (phang, khong hieu ung); bo cuc va chuc nang giu nguyen. Trang thai luu trong config.json.
- "Giam hieu ung chuyen dong" tat moi animation (cung theo cai dat animation cua Windows). May khong co GPU tu tat anh sang chay nen.
- Cua so khong vien, bo goc 20px tu ve (khong dung bo goc cua Windows), nen khong co blur desktop that.
- Component: Controls/LiquidGlassWindow, Panel, Card, Button, Slider, Toggle, Navigation, Popup.
  Skin: Themes/Skin.Glass.xaml va Themes/Skin.Flat.xaml (cung bo key); style: Themes/LiquidGlass.xaml.

## Tai mod (tab Mods)
- Chon instance (mac dinh la instance dang chon), chon nguon Modrinth hoac CurseForge, tim va bam "Cai".
- Chi hien mod dung phien ban Minecraft va mod loader cua instance (Quilt dung duoc mod Fabric). Instance chua co loader thi cai loader o tab Instance truoc.
- Tu cai kem mod phu thuoc bat buoc; cai lai thi go ban cu (so ghi mods-index.json).
- CurseForge can "CurseForgeApiKey" trong config.json.

## Mot launcher duy nhat va mo file .mrpack
- Mo launcher lan thu hai: hien canh bao "dang chay roi", dua cua so dang chay len truoc va thoat, khong mo them cua so.
- Bam dup file .mrpack: Windows chay launcher voi duong dan file. Neu launcher chua chay thi mo len va nhap modpack;
  neu dang chay thi file duoc chuyen sang launcher dang chay (khong hien canh bao). Luon hoi xac nhan truoc khi nhap.
- Dang ky file (.mrpack) ghi vao HKCU, khong can quyen admin. Bat/tat trong Cai dat -> Tich hop Windows.
  Neu Windows van mo bang app khac thi chon launcher trong "Cai dat ung dung mac dinh" (co nut mo san).
- Tab Mods luon hien danh sach mod; mod khong dung phien ban Minecraft/loader cua instance dang chon co dong canh bao vang.

## Hieu ung Liquid Glass theo video mau
- Vien trang manh, sang nhat o goc tren-trai va duoi-phai; nen duc sua; anh sang nhoe sat mep ben trong.
- Tan sac cau vong sat mep (hai lop chay nguoc chieu, lam nhoe) tren the, thanh menu va giot chi bao menu.
- Diem sang bam theo con tro khi re chuot len the/nut.
- Chuyen dong long: chi bao menu keo dan phu muc cu va moi roi co lai, nay vao dung muc; nut tron thanh truot bep va gian ngang khi an.
- Day la mo phong bang lop ve; chua co khuc xa that (be cong anh nen), viec do can pixel shader.
- Tat Liquid Glass hoac "Giam chuyen dong" thi cac hieu ung nay tat; may khong co GPU tu bo lop tan sac.

## Khuc xa that (Cai dat -> Giao dien -> "Khuc xa that")
- Khuc xa tinh bang C# tren CPU (Controls/LensRenderer.cs), khong dung pixel shader nen khong the lam sap launcher.
  Cong thuc: gan mep kinh anh nen bi keo cong theo do cong cua vien, moi kenh R/G/B lech mot luong khac nhau (tan sac),
  them anh sang phan chieu o vanh va chut suong trang. Chi dung lai khi vi tri/nen doi, nen thau kinh dung yen chi dung mot lan.
- Thau kinh (Controls/GlassLens) lay anh nen tu lop BackdropArt (chup mot lan thanh mang diem anh, dung chung), nen anh nen
  cang nhieu chi tiet thi khuc xa cang ro. Co the chon anh nen rieng trong Cai dat.
- Chi hoat dong khi Liquid Glass bat. Moi loi trong qua trinh dung deu duoc bat: thau kinh do tu tat, giu kinh thuong.
- Loi chua xu ly duoc ghi vao %AppData%\KittyDy0Launcher\crash.log; neu dang bat khuc xa thi tu tat khuc xa va thong bao.
- Ban pixel shader thu nghiem (khong dung trong ung dung) nam o tools/shader_experimental.

## Tab "Tai ve" (truoc la Mods)
- Chon 3 muc: Mods, Resource Pack, Modpack; nguon Modrinth hoac CurseForge; tim kiem; phan trang (20 muc/trang, nut Truoc/Sau).
- Moi the co nut "Tai ve" (Modpack: "Tai & nhap") va nut "Chi tiet": phien ban moi nhat/phu hop voi instance, ngay cap nhat,
  tep tai, ho tro Minecraft, loader, luot tai, giay phep, the loai, thong tin cap nhat (changelog), nut mo trang web.
- Mods cai vao mods/ (kem mod phu thuoc bat buoc); Resource Pack cai vao resourcepacks/; Modpack duoc tai ve roi nhap thanh instance moi.
- Anh dai dien tai nen, loi hoac thieu anh thi hien o chu cai dau tren nen mau (khong con muc co anh muc khong).

## Don gian hoa vien kinh (ban sua theo phan hoi)
- Bo cac lop vien gia (tan sac cau vong nhoe, anh sang nhoe, vien trong); chi con mot vien manh duy nhat + khuc xa that o vanh.
- Ban kinh bo luon bi kep theo nua canh ngan (GlassSurface.RenderRadius) nen nut nho bo tron deu; moi lop dung chung mot ban kinh.
- Dom sang/vet sang mac dinh chi hien khi KHONG co anh nen rieng; co anh nen thi phu mot lop lam toi nhe de chu de doc.
- Tat khung cham cham (FocusVisualStyle) quanh nut/cong tac; van co vong focus vang khi dung ban phim.

## Ca nhan hoa (Cai dat -> Ca nhan hoa)
- Vi tri thanh menu: Tren / Duoi / Trai / Phai (Trai/Phai xep muc theo chieu doc; chi bao di chuyen theo truc doc).
- Mau sac: Mau nhan, Mau nen, Mau chu (go ma HEX) + 6 bo mau co san; canh bao khi tuong phan chu/nen duoi 4.5:1.
- Do sang kinh (tang neu thay nut/the bi toi), Do dac cua the, Do dac cua thanh menu, Do manh khuc xa, Do toi phu len anh nen.
- Anh nen hien ca khi tat Liquid Glass; the o che do phang hoi trong de thay nen. Do toi anh nen chinh duoc.
- "Hieu ung tuong tac" (diem sang theo chuot, anh sang chay, nay/keo dan khi bam va khi chuyen menu): MAC DINH TAT. Bat lai trong Cai dat.
- "Dat lai ca nhan hoa" dua moi thu ve mac dinh. Moi thay doi ap dung ngay va duoc luu vao config.json.

## Tai ve: chon dung phien ban
- O "Phien ban Minecraft (vd 1.12.2)": loc ket qua theo phien ban do (bo trong = theo instance dang chon). Co san instance dung phien ban thi tu chon.
- Chi tiet -> "Chon phien ban de cai" liet ke cac ban (MC, loader, ten, ngay) -> "Cai ban da chon".
- Chua co instance dung phien ban Minecraft thi hoi tao instance moi (kem Forge/Fabric...) roi cai vao do; khong tu cai vao instance sai phien ban.
- Modpack: chon phien ban trong Chi tiet, hoac loc Minecraft roi bam "Tai & nhap" de lay ban moi nhat hop phien ban.
- Nut mo thu muc instance / mods / resourcepacks o Trang chu va tab Tai ve.
