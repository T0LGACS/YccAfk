# YccAfk

AFK yönetimi: kıpırdamayan oyuncu uyarılır, sonra izleyiciye alınır. Kalabalıkta uzun süre izleyicide bekleyen atılır. Aynı noktada kamp yapan uyarılıp tokatlanır (bomba kuruluyken ve savunan takımda sayılmaz). Adminler muaf.

> AFK, spectator and anti-camp manager with warnings before punishment.

[YCC TEAM](https://yccteam.com.tr) CS2 sunucusu için sıfırdan yazıldı ve sunucuda her gün çalışıyor. Başka eklentilerden kod alınmadı.

## Gereken

- CS2 sunucusu, [Metamod:Source](https://www.sourcemm.net/) ve [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (1.0.371 ve üstü)

## Kurulum

1. Sunucuda [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) ve [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) (with-runtime sürümü) kurulu olmalı.
2. [.NET 10 SDK](https://dotnet.microsoft.com/download) ile derle: `dotnet build -c Release`
3. `bin/Release/net10.0/` içindeki her şeyi sunucuda `addons/counterstrikesharp/plugins/YccAfk/` klasörüne kopyala.
4. Sunucuyu başlat. Ayar dosyası ilk açılışta `addons/counterstrikesharp/configs/plugins/YccAfk/YccAfk.json` olarak kendiliğinden oluşur; düzenleyip sunucuyu yeniden başlat.

## Notlar

Bütün süreler, uyarı sayıları ve cezalar ayar dosyasından değiştirilir.

## Lisans

MIT. Kullanabilir, değiştirebilir, paylaşabilirsin; lisans dosyasını koruman yeterli.

Sunucu: **yccteam.com.tr** · Site: https://yccteam.com.tr
