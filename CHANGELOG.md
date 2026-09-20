# Changelog

## [0.6.0](https://github.com/hoobio/steward-companion/compare/v0.5.0...v0.6.0) (2026-09-20)


### Features

* ✨ show a friendly addon name with the installed version beneath it ([ae870ea](https://github.com/hoobio/steward-companion/commit/ae870ea21076e5f546050393226a903d55133fd6))


### Bug Fixes

* 🐛 download GitHub addon zips from their absolute release URL ([27c8dce](https://github.com/hoobio/steward-companion/commit/27c8dce029868ffe90690ee021b5a3ea06fe6ca5))
* 🐛 keep Retry in the actions column and show the update state as a glyph beside the version ([f234580](https://github.com/hoobio/steward-companion/commit/f23458017223d1a8269314c3f6540029e628d4b7))

## [0.5.0](https://github.com/hoobio/steward-companion/compare/v0.4.0...v0.5.0) (2026-09-20)


### Features

* ✨ name channels release, pre-release and development, add GitHub prerelease channel and repo icons, and stack release notes back to the last minor ([79ea6d1](https://github.com/hoobio/steward-companion/commit/79ea6d1081acda4464be44fc2308a01ebaba14c4))


### Bug Fixes

* 🐛 close a running Steward before the MSI upgrades it and launch it afterwards ([b566a8e](https://github.com/hoobio/steward-companion/commit/b566a8ec1506baac60922ab546ba33f9d0a692ce))
* 🐛 make the About card's check button run the update check instead of opening GitHub ([1538465](https://github.com/hoobio/steward-companion/commit/1538465392caaf781316792551ad7480915fcbc3))
* 🐛 replace an addon folder that is a git checkout instead of refusing ([37085d2](https://github.com/hoobio/steward-companion/commit/37085d239d95ecdffb4bc4e46a552984c3ba337c))
* 🐛 retry an unreachable guild API every minute, offer Retry, and disable Sync meanwhile ([5105d29](https://github.com/hoobio/steward-companion/commit/5105d29d37c89880cf3a12472d41d8ec548016b4))

## [0.4.0](https://github.com/hoobio/steward-companion/compare/v0.3.0...v0.4.0) (2026-09-20)


### Features

* ✨ add a start with Windows toggle that launches to the tray ([ae4531c](https://github.com/hoobio/steward-companion/commit/ae4531ce791de1677323cf6844fd2148db18104c))
* ✨ check for Steward updates and install them silently from GitHub releases ([d86c853](https://github.com/hoobio/steward-companion/commit/d86c8536a807696dc49176a8df8acb2722c67095))
* ✨ hide addons managed elsewhere and drop the banner's check again button ([bea96c2](https://github.com/hoobio/steward-companion/commit/bea96c20bc7f1380f5b67743ae1a8ef89be262d5))
* ✨ install the Steward addon automatically and list it first ([400fac0](https://github.com/hoobio/steward-companion/commit/400fac062d449abac7271121481571f6ed5fe8c6))
* ✨ label development builds and keep start with Windows to GitHub releases ([e9213a4](https://github.com/hoobio/steward-companion/commit/e9213a4dba62e805185f7a83a689f0b05531c9cd))
* ✨ manage RestedXP from its tagged GitHub releases ([beae008](https://github.com/hoobio/steward-companion/commit/beae0087d94b19b9eab294ac36c439b2f6e46080))

## [0.3.0](https://github.com/hoobio/steward-companion/compare/v0.2.0...v0.3.0) (2026-09-19)


### Features

* ✨ apply addon updates whether or not the client is running ([937a18d](https://github.com/hoobio/steward-companion/commit/937a18d98809373fbbbe6228e9059193b6b3faf3))


### Bug Fixes

* 🐛 restore the window on a single or double click of the tray icon ([d09b385](https://github.com/hoobio/steward-companion/commit/d09b385030ec78ccbc55d2b0101979f91cd2bdef))

## [0.2.0](https://github.com/hoobio/steward-companion/compare/v0.1.0...v0.2.0) (2026-09-19)


### Features

* ✨ apply updates when the client is closed and show when it is running ([f8d771a](https://github.com/hoobio/steward-companion/commit/f8d771ae8fad8e74bcec319fe0e8f269109db407))
* ✨ Ayu Mirage corner washes on the window ground ([44c438b](https://github.com/hoobio/steward-companion/commit/44c438be221b4c24b221bbe16e4d5a85956a3aea))
* ✨ check for addon updates on the recheck timer and at startup ([473b253](https://github.com/hoobio/steward-companion/commit/473b2530cd83091238b8125a33c65dcc94602b1b))
* ✨ custom title bar, dark Ayu palette and a signed-out gate ([c13cbd5](https://github.com/hoobio/steward-companion/commit/c13cbd5fbc3d4d6a106ed2f20d02b53ebdfa4a84))
* ✨ default new addons to beta and never delete a git working tree on update ([a71012d](https://github.com/hoobio/steward-companion/commit/a71012d4ad9f6c6e9b5a0b869eb15c03806896fd))
* ✨ find the running client's start time and judge saved-variables freshness ([ad5ca89](https://github.com/hoobio/steward-companion/commit/ad5ca89230b8071dc56e3a4ca6980e241aaf79ec))
* ✨ install cards, summary banner and a settings page ([149fcda](https://github.com/hoobio/steward-companion/commit/149fcdaa34cf8ca5a19c4b966725d7a09cc938da))
* ✨ load addon icons from addon.hoobi.io, real arrows and 32px inline controls ([8ff06f6](https://github.com/hoobio/steward-companion/commit/8ff06f68ed963e21db0b1829f07d26535bbe934f))
* ✨ manage the Steward addon alongside HoobiScripts ([585be42](https://github.com/hoobio/steward-companion/commit/585be42d50dc7b6935b0b9cf89cd6ff9b0c1fd3d))
* ✨ manage World of Warcraft: Forever installs only ([6c9fe54](https://github.com/hoobio/steward-companion/commit/6c9fe540a574288b0499caf302a3a5fc434a4df3))
* ✨ NavigationView shell with Addons, Sync and Settings ([c384b3a](https://github.com/hoobio/steward-companion/commit/c384b3a9cd360523c550a7e99bdb7f5ea65719a9))
* ✨ per-addon release channels with a one-time migration ([da68576](https://github.com/hoobio/steward-companion/commit/da685761df552fc6eeeee4b06d5a037bb0d0a54c))
* ✨ probe every channel per addon and default to the highest with a release ([0dbbaba](https://github.com/hoobio/steward-companion/commit/0dbbabaea92bf795702ab72d4e1929c9cd79a404))
* ✨ re-read saved variables on the timer and when the client exits ([75e85ff](https://github.com/hoobio/steward-companion/commit/75e85ffaab7c20b927078fbd49e5ae85f142e986))
* ✨ read the Steward addon's SavedVariables into roster, loot and attendance ([c15cc1d](https://github.com/hoobio/steward-companion/commit/c15cc1d4edf81897c42ac5121d4002b9c1691ace))
* ✨ show the installed addon's own icon on its row ([8f0c753](https://github.com/hoobio/steward-companion/commit/8f0c7532a58f505843be2c455f16b2580e7b60ad))
* ✨ sign in through the default browser with a loopback callback ([0a6ce12](https://github.com/hoobio/steward-companion/commit/0a6ce1293fbc4083f261a8ae9d4c0bcca8a58c73))
* ✨ stamp the app icon on the exe, windows and installer ([d266fd9](https://github.com/hoobio/steward-companion/commit/d266fd91a506af65cfa0a6173d38d91e9ebadb7d))
* ✨ Sync page against an in-memory guild API fake ([1c322f0](https://github.com/hoobio/steward-companion/commit/1c322f0f00060a4048de8cdcc9507d72e54211ce))
* ✨ tray icon with minimise and close to tray ([920fd5f](https://github.com/hoobio/steward-companion/commit/920fd5fe63ce43994106c8fb0db37f419854de2d))
* ✨ write the generated Steward sync file the addon loads ([aa29809](https://github.com/hoobio/steward-companion/commit/aa298093314357191c1d926628969750ddbb941a))


### Bug Fixes

* 🐛 centre the gate on the whole window and drop the doubled settings labels ([87c485a](https://github.com/hoobio/steward-companion/commit/87c485a2e3e6ac487e0868f3c1bf2d550518a0fb))
* 🐛 centre the settings column at every window width ([d58b224](https://github.com/hoobio/steward-companion/commit/d58b224abb9d4ac79f1cff5ce5799e445950899a))
* 🐛 keep resolved addons on a partial probe failure and show the Discord handle ([b04d118](https://github.com/hoobio/steward-companion/commit/b04d11830856853c110f1561caf27f24c8506af7))
* 🐛 point at api.hoobi.io/guild after the SWA cutover ([7686363](https://github.com/hoobio/steward-companion/commit/76863638487a9d0dea1ddb5e20d0c7dd81f0fc1f))
* 🐛 stale-channel notice, null state keys and the row's no-releases text ([767e53c](https://github.com/hoobio/steward-companion/commit/767e53c5427934c7e1f5866fec25887a872a64fb))
* 🐛 survive offline rechecks, atomic state writes and a clean tray quit ([c468593](https://github.com/hoobio/steward-companion/commit/c468593e4b4e28cd90451777283433ba2f72ffae))
* 🐛 treat an empty release channel as empty rather than a fault ([133e6ba](https://github.com/hoobio/steward-companion/commit/133e6ba455ee27afe81f25f0f26573a2a05b68d1))
