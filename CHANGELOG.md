# Changelog

## [0.12.0](https://github.com/hoobio/steward-companion/compare/v0.11.0...v0.12.0) (2026-09-26)


### Features

* ✨ answer Hoobi addon version pings from the generated StewardGuides addon ([773a8f9](https://github.com/hoobio/steward-companion/commit/773a8f91ca1627f9b3a7ca43bf2a5e36365404c5))
* ✨ drop the develop addon channel, migrate stored selections to pre-release ([0d1f738](https://github.com/hoobio/steward-companion/commit/0d1f738ca2d62e1d44e23936e9b71dc2191172bb))
* ✨ label Store pre-release builds as Steward (Pre-release) ([079b4f8](https://github.com/hoobio/steward-companion/commit/079b4f845d212836fe157f2c638ab87799a04694))
* ✨ map addon-observed professions into the character sync push ([421999a](https://github.com/hoobio/steward-companion/commit/421999a1651facc9f944b1b71738dcba5e221e8c))
* ✨ push characters on startup, Refresh and Sync now, with a per-install Send now ([1133e5f](https://github.com/hoobio/steward-companion/commit/1133e5fdb85a6a56ae81c4b6921a6da9c8dfe0f6))
* ✨ push observed characters to gigagrug's character sync endpoint ([4a9362a](https://github.com/hoobio/steward-companion/commit/4a9362a0d3a227fccffa8642670f9974a02bfa10))
* ✨ show the character push in each install's Roster row, and force it from Sync now ([f192f53](https://github.com/hoobio/steward-companion/commit/f192f53d464a33e24552fa49e817158e6a92fec8))
* ✨ sync each roster member's linked main character ([69987c1](https://github.com/hoobio/steward-companion/commit/69987c13c3eee35028ffd034978747524e33248b))
* ✨ sync the addon's guild rank list to gigagrug ([0887998](https://github.com/hoobio/steward-companion/commit/0887998735a028ca9ade3f7b637519955c3b6036))
* ✨ sync the addon's recipe catalogue alongside characters ([58529be](https://github.com/hoobio/steward-companion/commit/58529bec6453f76504b635259ce986be47fbd82b))


### Bug Fixes

* 🐛 fingerprint canonical character data without observedAt and scannedAt ([f39dd1a](https://github.com/hoobio/steward-companion/commit/f39dd1ac3436067bbfd24d9637e2a030bd638c22))
* 🐛 harden character sync against overflowing timestamps and gate Sync now on the sync feature ([52f8724](https://github.com/hoobio/steward-companion/commit/52f8724daf1fe9b0e023645b02946500a253879e))
* 🐛 harden the character sync push against retries, 401s and stale data ([db47cb6](https://github.com/hoobio/steward-companion/commit/db47cb6efe7a7dea366cfda9292fe161ca0f4708))
* 🐛 keep character sync records out of the shared state.json so an older build cannot drop them ([80cb3b5](https://github.com/hoobio/steward-companion/commit/80cb3b5729c1db6ca2149aa3f3a47a99aa835640))
* 🐛 keep the fallback recipe catalogue per user and guild, and stop rebuilding unchanged character rows ([5178622](https://github.com/hoobio/steward-companion/commit/51786228f61ccf156cbad627f92737601fd8fd8e))
* 🐛 key the Sync page on real character exports, not mock roster/loot/attendance ([cf79f53](https://github.com/hoobio/steward-companion/commit/cf79f53b70a82d9229f426ba480cad8b6238c74e))
* 🐛 open the Store's updates page when an update is available ([15a2d97](https://github.com/hoobio/steward-companion/commit/15a2d97c5c091f11fb0def9bb3c54b4f3c8eb981))
* 🐛 read the guild rank list the client saves as positional entries ([9a46142](https://github.com/hoobio/steward-companion/commit/9a461427aa2185ae60c35212680e5b64105743fd))
* 🐛 restore the Sync page's dataset icons ([718afd5](https://github.com/hoobio/steward-companion/commit/718afd5ac2e5fbd18d96919f8490aa479992a384))
* 🐛 shorten pre-release versions to &lt;version&gt;-&lt;sha&gt; so they fit the addon row ([10b440b](https://github.com/hoobio/steward-companion/commit/10b440b88fb1816130b46e203eb423ef811c077a))
* 🐛 strip mock data from the Sync page and cut duplicate send buttons ([4477851](https://github.com/hoobio/steward-companion/commit/44778515baacc29d8d1ed629d5bff73474197ce4))


### Performance Improvements

* ⚡ gzip-compress the character sync request body ([75daa98](https://github.com/hoobio/steward-companion/commit/75daa98448fb0bd4cd34ab8b178af9dae1a62673))

## [0.11.0](https://github.com/hoobio/steward-companion/compare/v0.10.0...v0.11.0) (2026-09-26)


### Features

* ✨ add an Auto-update addons setting ([17656aa](https://github.com/hoobio/steward-companion/commit/17656aa1e4958bf4589fb0f247e4a10c902a017a))
* ✨ check the Store when packaged and offer the MSI-to-Store switch ([bf72bd8](https://github.com/hoobio/steward-companion/commit/bf72bd8ce243b7dd4b4ec439e15d431f27412499))
* ✨ gate addons, guides and sync on gigagrug's per-user features ([db52c5e](https://github.com/hoobio/steward-companion/commit/db52c5e8258612ab701e72ec460e1842b094a2a8))
* 💄 style the sign-in callback page with the house design system ([17b832d](https://github.com/hoobio/steward-companion/commit/17b832d88ef8cc55be62590d787f1edfe2682968))


### Bug Fixes

* 🐛 check RestedXP releases through Steward's own service so GitHub's rate limit no longer blocks updates ([1e565ad](https://github.com/hoobio/steward-companion/commit/1e565adf49a182128dc7d98e4c05b7380f5e2606))
* 🐛 run a single instance of Steward at a time ([e5da730](https://github.com/hoobio/steward-companion/commit/e5da730de8ef41adc065a602fc9499bba5ee2cd3))
* 🐛 hide the RestedXP sign-in until the guides feature is confirmed ([0c9c96a](https://github.com/hoobio/steward-companion/commit/0c9c96a92185e2d48644f9c02f048665bdc1803b))
* 🐛 let any addon holder pick its release channel ([4a5a153](https://github.com/hoobio/steward-companion/commit/4a5a153ffbfbe7a7b0dfea1cf173f0b06d38abcd))
* 🐛 load the tray icon through ms-appx in the Store build ([ecbd14a](https://github.com/hoobio/steward-companion/commit/ecbd14ac7cb356fc508ac655db02363f38391635))
* 🐛 reconcile addon rows and gates on feature change ([ad4c311](https://github.com/hoobio/steward-companion/commit/ad4c31191ae56a2baf490eff416a21c7146331fb))
* 🐛 spin and disable the refresh buttons while a refresh runs ([1fff294](https://github.com/hoobio/steward-companion/commit/1fff294fb6212fcf4bf388c57457b34c6dfc39d9))
* 🐛 update the sync cards in place so a refresh stops replaying their open animation ([3def7df](https://github.com/hoobio/steward-companion/commit/3def7dfa48e4ca9cef341993d579c4d3fb1bd75e))
* 💄 align settings expander rows with their header icon ([707dd7f](https://github.com/hoobio/steward-companion/commit/707dd7fe7c3c51700c9c92b84a61aad2abde76fe))
* 💄 hide Sync behind the steward feature and contrast the role pill ([d694239](https://github.com/hoobio/steward-companion/commit/d6942393543008622a9aa829fde8dacb0379f627))
* 💄 keep addon row buttons visible at narrow window widths ([6cc2842](https://github.com/hoobio/steward-companion/commit/6cc28423009d31253ed3e4ec454a416734607efb))
* 💄 set the expander row padding on the cards themselves ([7d9f242](https://github.com/hoobio/steward-companion/commit/7d9f2426fbba23381943167c96f2502785abfa18))
* 💄 tighten the settings expander row's right edge and widen the install row gap ([66c1f4e](https://github.com/hoobio/steward-companion/commit/66c1f4ef98f7578353cc94dda8e67c06a336918e))
* 💄 title the guides page RestedXP Guides ([2359562](https://github.com/hoobio/steward-companion/commit/23595621bc9785f195f5884c77a5b443183ee480))

## [0.10.0](https://github.com/hoobio/steward-companion/compare/v0.9.0...v0.10.0) (2026-09-24)


### Features

* ✨ carry each origin's colour through to the addon ([2fa43c2](https://github.com/hoobio/steward-companion/commit/2fa43c2a142582986fde9b7c0e085d0097bc3268))


### Bug Fixes

* 🐛 even out the Addons page spacing and say when updates were last checked ([4f6794b](https://github.com/hoobio/steward-companion/commit/4f6794b5eea23c8cf31b8d215328738dbcd2f8b9))
* 🐛 line the page title up with its navigation item ([1e6d6f9](https://github.com/hoobio/steward-companion/commit/1e6d6f9110cf14cf8e5cfeda02db6d6d922ebca7))
* 🐛 restore and raise the window on a tray icon click, including after a tray launch ([f408cd0](https://github.com/hoobio/steward-companion/commit/f408cd0dcba0e68fd1a6dbfc3649db60103c5fed))
* 🐛 retry the admin check when Retry is clicked ([8501181](https://github.com/hoobio/steward-companion/commit/85011813d75a57d821643d2549b9057371ce3897))
* 🐛 rewrite the roster file after an addon update and stop Write again writing sample data ([8aebd22](https://github.com/hoobio/steward-companion/commit/8aebd228be2e5d1bb3d5ca10932219342c8ee1f1))
* 🐛 stop the dropdown hover flash and bring controls in line with hoobi-design ([95f54ff](https://github.com/hoobio/steward-companion/commit/95f54ff93938ae694b18ba1e48ed05e87768b5e9))

## [0.9.0](https://github.com/hoobio/steward-companion/compare/v0.8.3...v0.9.0) (2026-09-21)


### Features

* ✨ rename the addon update channel from development to develop ([ef3453f](https://github.com/hoobio/steward-companion/commit/ef3453ff4903fe6de93da116450f7db7b888d051))
* ✨ split minimise to tray from close to tray, and let the installer end a tray-parked instance ([24f1a24](https://github.com/hoobio/steward-companion/commit/24f1a2465b3b5077c7c35dec89aeb1c2f689c9b0))
* ✨ stop a user with no officer role at the sign-in gate ([c1adc2a](https://github.com/hoobio/steward-companion/commit/c1adc2a3eac0ce7eef2c2b952a90e050de7e4e99))
* ✨ take self-update from the manifest host and poll it every minute ([1987db0](https://github.com/hoobio/steward-companion/commit/1987db0c92b49a5d61a725461713a4102b40d4f8))
* 🎸 carry the guild's ordered status catalogue into StewardSync.lua ([18f04c1](https://github.com/hoobio/steward-companion/commit/18f04c19a9baa1f677c030da96a02895f4d0012b))
* 🎸 choose the guild to sync and carry the Discord avatar into the game ([d733cd5](https://github.com/hoobio/steward-companion/commit/d733cd524f66408749c4b0f1307eb6b4e374a074))
* 🎸 pull the gigagrug guild roster and Discord member list into StewardSync.lua ([b92958f](https://github.com/hoobio/steward-companion/commit/b92958f11d9f6936c2ada59eb9448db4c7d35880))
* 🎸 pull the gigagrug roster on every refresh pass and write StewardSync.lua ([9aa4f6b](https://github.com/hoobio/steward-companion/commit/9aa4f6bdb2dba0d810c4cf730e5fa94de64f9582))


### Bug Fixes

* 🐛 cut the trailing explanations out of the app's banners and empty states ([cb55efc](https://github.com/hoobio/steward-companion/commit/cb55efc23856ece3b891b25ff2f6050a53197cc4))
* 🐛 detect GitHub rate limits and back off until reset ([f9bc2c5](https://github.com/hoobio/steward-companion/commit/f9bc2c5e288cd8f0ab4608ac83b1352a659626ec))
* 🐛 keep the addon row name column from collapsing under a long action button ([df4dfbe](https://github.com/hoobio/steward-companion/commit/df4dfbe77cbe90457dee2140811a5df0bb9ce9ce))
* 🐛 show the guild's Discord icon in game, not the signed-in user's avatar ([3ddafba](https://github.com/hoobio/steward-companion/commit/3ddafbaa254f5d085a9094e74d87836b95623a43))

## [0.8.3](https://github.com/hoobio/steward-companion/compare/v0.8.2...v0.8.3) (2026-09-20)


### Bug Fixes

* 🐛 pin the shipped .NET runtime at 10.0.12 and write every file on install regardless of version ([9c50acf](https://github.com/hoobio/steward-companion/commit/9c50acff36eea907da88c97816a1ead3ee2c23ae))

## [0.8.2](https://github.com/hoobio/steward-companion/compare/v0.8.1...v0.8.2) (2026-09-20)


### Bug Fixes

* 🐛 confirm a guide import as soon as the game writes the StewardGuides saved variable ([0140851](https://github.com/hoobio/steward-companion/commit/0140851dc18463a1df47e958de0818ba52804b19))

## [0.8.1](https://github.com/hoobio/steward-companion/compare/v0.8.0...v0.8.1) (2026-09-20)


### Bug Fixes

* 🐛 import every guide string on every login and leave an unchanged addon file alone ([53cd7e2](https://github.com/hoobio/steward-companion/commit/53cd7e2c901d97e40d8353e0f745269ce90d099b))
* 🐛 let a manual Refresh see a release published inside the 15-minute GitHub cache ([a39c62a](https://github.com/hoobio/steward-companion/commit/a39c62af3f2229ed0a49b0ebcf7f08d664cd7a4b))
* 🐛 let Check for a new version see a release published inside the GitHub cache window ([77ab17e](https://github.com/hoobio/steward-companion/commit/77ab17eede273224bdfc206b768d088f14ff6c23))
* 🐛 offer the latest release to any pre-release build on the release channel ([94da252](https://github.com/hoobio/steward-companion/commit/94da252d79db69166d6163ac2be452490a0a8411))
* 🐛 pick the newest GitHub release by published date, not list position ([2ae8c65](https://github.com/hoobio/steward-companion/commit/2ae8c65fb54794554e1befce181be2a611c849bd))
* 🐛 read only the current import's status by clearing the importer history first ([466c7c3](https://github.com/hoobio/steward-companion/commit/466c7c390ced27dc6916c34a73fdc6b09e7ebc60))
* 🐛 report why a guide did not import, in chat and on the Guides row, and check the BattleTag first ([1962a64](https://github.com/hoobio/steward-companion/commit/1962a64f084ace879c336e405141b3f6ba1dff0b))
* 🐛 rewrite the StewardGuides addon whenever its rendered body differs from the file, not only when a guide changed ([94344f1](https://github.com/hoobio/steward-companion/commit/94344f11dcb75aa3e344618a475d1bc30a81c6be))
* 🐛 show the BattleTag after a mid-session sign-in and refuse a guide bound to no tag ([6e2686b](https://github.com/hoobio/steward-companion/commit/6e2686bc4699e6c593a8d477ecda62f2516d5886))
* 💬 say what to check when Battle.net is not connected, in chat and on the Guides row ([d9615f7](https://github.com/hoobio/steward-companion/commit/d9615f7102a6e1368af057109c23a133b558a2fe))
* 🔐 keep the RestedXP session across restarts with an offline Keycloak token ([81a13bc](https://github.com/hoobio/steward-companion/commit/81a13bc39e900af2394d1158e87655e20f03dca9))
* 🛡️ answer off from RXPGuides' bag predicates while its settings profile is missing ([c3514e4](https://github.com/hoobio/steward-companion/commit/c3514e4b8e96ae4798bfb9fd2d4d5faaad1b60da))

## [0.8.0](https://github.com/hoobio/steward-companion/compare/v0.7.3...v0.8.0) (2026-09-20)


### Features

* ✨ add the Guides page and the RestedXP sign-in dialog ([7da86a1](https://github.com/hoobio/steward-companion/commit/7da86a1cc8b8642daeec02457c8fb1adbf9e2331))
* ✨ clear the /reload hint once a SavedVariables write shows the client reloaded after the update ([6acb00a](https://github.com/hoobio/steward-companion/commit/6acb00a432edb47daaa8fbdd168cc519268dc3e6))
* ✨ confirm guide imports from the StewardGuides saved variable ([e58ca45](https://github.com/hoobio/steward-companion/commit/e58ca45fca9d39fa78eeab24adbe04ef1a9a2d15))
* ✨ explain a guide row gated by client with a tooltip and link the release checklist ([3b0bea7](https://github.com/hoobio/steward-companion/commit/3b0bea74b0735e32a945db07e357c30c730ab225))
* ✨ keep purchased RestedXP guides current in game ([03e9810](https://github.com/hoobio/steward-companion/commit/03e981048fb175d1ae9296583b8760deec0bbab4))
* ✨ offer start with Windows from the installer, on by default ([02f9310](https://github.com/hoobio/steward-companion/commit/02f93104a1cff21b7cb8f3f2f6cb5633e80d7cf4))
* ✨ pick the app's own update channel and hide a single-channel addon picker ([37d07fc](https://github.com/hoobio/steward-companion/commit/37d07fc1e6522a94b2474b2c95f161a4329efee7))
* ✨ ship purchased guides through a generated StewardGuides addon and keep several per install ([9ddb1a3](https://github.com/hoobio/steward-companion/commit/9ddb1a3c698d6f1fb5f92a03927654c1a4dfce22))


### Bug Fixes

* 🐛 harden the RestedXP pass against overlaps, bad bodies and the unreachable-API retry ([c0ae56f](https://github.com/hoobio/steward-companion/commit/c0ae56fd5b994f71eff075fe852de7cc4cbe7eaa))
* 🐛 renew the RestedXP session near the refresh token's end and refresh on demand before a download ([958035a](https://github.com/hoobio/steward-companion/commit/958035ad034ae4717def1c92f0546dfe59fb77cd))
* 💬 rewrite user-facing copy without agency verbs and filler ([0f8e0f9](https://github.com/hoobio/steward-companion/commit/0f8e0f98aaa040f639ec655189f5a4de8848bd86))


### Performance Improvements

* ⚡ poll the addon manifests every 5 minutes and hold GitHub calls to one per repo per 15 ([cd4a39a](https://github.com/hoobio/steward-companion/commit/cd4a39aac3f2cf6f05dc24c97f5e43243c840bcb))

## [0.7.3](https://github.com/hoobio/steward-companion/compare/v0.7.2...v0.7.3) (2026-09-20)


### Bug Fixes

* 🐛 show when an addon was last updated in the up-to-date tooltip ([73a1bbd](https://github.com/hoobio/steward-companion/commit/73a1bbd6eda437b244906d1df27b942f29131511))

## [0.7.2](https://github.com/hoobio/steward-companion/compare/v0.7.1...v0.7.2) (2026-09-20)


### Bug Fixes

* 🐛 fetch one cached GitHub listing per repo and stop re-polling GitHub while the guild API is unreachable ([d128c56](https://github.com/hoobio/steward-companion/commit/d128c56f6f28697518c922750d345ac5a7c72d4a))

## [0.7.1](https://github.com/hoobio/steward-companion/compare/v0.7.0...v0.7.1) (2026-09-20)


### Bug Fixes

* 🐛 put the update banner's button at the right and link the release notes ([80c636b](https://github.com/hoobio/steward-companion/commit/80c636b58900f74513b5aa8b9586ae03f0318217))

## [0.7.0](https://github.com/hoobio/steward-companion/compare/v0.6.0...v0.7.0) (2026-09-20)


### Features

* ✨ brand the MSI with Steward dialogs and skip the licence page ([7c5de0f](https://github.com/hoobio/steward-companion/commit/7c5de0fe403a1a508d32e518619bf6dfd6c33835))

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
