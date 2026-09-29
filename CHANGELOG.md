# Changelog

## [0.17.0](https://github.com/hoobio/steward-companion/compare/v0.16.0...v0.17.0) (2026-09-29)


### Features

* ✨ add a Get addons dialog that installs from CurseForge ([c5a6ffd](https://github.com/hoobio/steward-companion/commit/c5a6ffd998ad71db3b7ca5b2e8b2a0a4879db19f))
* ✨ clean up installs whose folder is gone ([bc96eb0](https://github.com/hoobio/steward-companion/commit/bc96eb030182357af90bae143b23b6745da3fc76))
* ✨ identify installed addons by CurseForge project ID and fingerprint ([15eab71](https://github.com/hoobio/steward-companion/commit/15eab71e79af0353d097109aa46a6a0971f7770f))
* ✨ install addons from CurseForge install links ([88ec163](https://github.com/hoobio/steward-companion/commit/88ec16390c52e8cb75ac1a86e5d58513a6f8edfc))
* ✨ install CurseForge manifests with SHA-1 and several folders ([5e9218f](https://github.com/hoobio/steward-companion/commit/5e9218f1fe3ff17c46f63b1388f0ef4414fe3f9a))
* ✨ turn installed CurseForge addons into CurseForge rows that update ([4bef6fe](https://github.com/hoobio/steward-companion/commit/4bef6fe5f4c9362b1e2c3c2477a63c29a0323274))


### Bug Fixes

* 🐛 centre the changelog icon on the version text ([f104895](https://github.com/hoobio/steward-companion/commit/f104895621f5dd3fe81a55fe7b280dabe771926d))
* 🐛 choose the guild from the account menu and ask once when there are several ([f85697b](https://github.com/hoobio/steward-companion/commit/f85697b9e18a312a9b38bd074614d77d2aa0fb59))
* 🐛 give dropdowns translucent tinted acrylic and rounded corners ([2e37460](https://github.com/hoobio/steward-companion/commit/2e37460a046d6c1115ad6f3d33bcfdb0f931c64b))
* 🐛 give every dialog the same themed style ([12828ba](https://github.com/hoobio/steward-companion/commit/12828baf1722b610d7b330cc6a1fac00744f3f69))
* 🐛 give the Sync page's running notice its own line ([eace331](https://github.com/hoobio/steward-companion/commit/eace3312e64abcf42fb2e3bb09089db389fbafd4))
* 🐛 keep CurseForge addons in their own file so an older build cannot drop them ([15a1013](https://github.com/hoobio/steward-companion/commit/15a1013799ef4c26acbfce488721552946391074))
* 🐛 keep refresh reachable and respect the server's guild choice ([45c8643](https://github.com/hoobio/steward-companion/commit/45c8643e47d36cb00f6242f03554920f5f969998))
* 🐛 keep the title bar buttons clickable after their content changes ([a292735](https://github.com/hoobio/steward-companion/commit/a29273527b84c7953f8c22b10ff24717cfd4e9ef))
* 🐛 keep the update prompt for the rest of the run once a route is retired ([935712c](https://github.com/hoobio/steward-companion/commit/935712c730fc24a604b06777bf4bf7bf40b910de))
* 🐛 keep versions and statuses whole in a narrow Addons table ([3c4e507](https://github.com/hoobio/steward-companion/commit/3c4e507ff090a523e9db5ba873db290671f5bf89))
* 🐛 let dropdowns keep their shadow and a translucent tint ([f306639](https://github.com/hoobio/steward-companion/commit/f306639d396ff2b06626642a3f74e05f108027bd))
* 🐛 let the delete guard accept an addon's own flavour TOC ([8182d40](https://github.com/hoobio/steward-companion/commit/8182d4091d3c82c34805159efb60cafb30d8ffc2))
* 🐛 make the Added by you pill readable ([e787610](https://github.com/hoobio/steward-companion/commit/e7876107f3617faae8d79c8956a8fe5d2f7289e7))
* 🐛 move refresh and the folder shortcut out of the Addons title row ([8ecb4d3](https://github.com/hoobio/steward-companion/commit/8ecb4d31471060aa7e07a312218df634ec5203f4))
* 🐛 name matched CurseForge rows after their CurseForge project ([9d344d2](https://github.com/hoobio/steward-companion/commit/9d344d23fe7a4bf5f35d1485c6bc4bbc3f4c06a8))
* 🐛 open the guild submenu on hover ([00b8ad1](https://github.com/hoobio/steward-companion/commit/00b8ad1529fff937b1eacc26225c0ab5bda93afa))
* 🐛 record SHA-1 installs as sha1 and uninstall every folder an addon ships ([e224410](https://github.com/hoobio/steward-companion/commit/e224410fd375d434f5d8b9398dc4e2aa1dd4f496))
* 🐛 refresh the Store catalogue when Check for updates is pressed ([8de4de4](https://github.com/hoobio/steward-companion/commit/8de4de4b8fe2f22f54e73bdc752dc9a7aef13ab7))
* 🐛 show an update prompt when gigagrug retires a route this build calls ([5eb283a](https://github.com/hoobio/steward-companion/commit/5eb283aa50dff432eef685b6b45b041acbd3b254))
* 🐛 show and remove installs whose folder is gone ([1550805](https://github.com/hoobio/steward-companion/commit/155080539d13abcf61dd5bd3116890f20632b89a))
* 🐛 show each guild's own role in the guild list ([8079704](https://github.com/hoobio/steward-companion/commit/8079704fc7bce7e864a9afba610bd116502d8f3b))
* 🐛 show the /reload notice only when there is new data to load ([6cac7e0](https://github.com/hoobio/steward-companion/commit/6cac7e024ca20bac543f0a1551b824841555f52e))
* 🐛 show the client version in the install picker instead of the path ([b25f45d](https://github.com/hoobio/steward-companion/commit/b25f45d27598c002ec29ceafd6acc0c6c7e75512))
* 🐛 show the row action in the Status column ([394df47](https://github.com/hoobio/steward-companion/commit/394df478d193ecdd44a487c6d6fe988f9762921c))
* 🐛 show the running dot on the install row, not twice ([0393d2c](https://github.com/hoobio/steward-companion/commit/0393d2ca5daa8742404090f2d299c7982c523323))
* 🐛 show the selected install's guides as one card ([4d4c6f4](https://github.com/hoobio/steward-companion/commit/4d4c6f46c7b89268c5d7f36035d340b1749ad40e))
* 🐛 show the selected install's sync datasets as one card ([f467b0f](https://github.com/hoobio/steward-companion/commit/f467b0f64470c839a0f065c839442831cec61624))
* 🐛 stop the Get addons search and source picker overlapping ([c660fd6](https://github.com/hoobio/steward-companion/commit/c660fd6c263cb037b86f41b68077b06e86c6198a))
* 🐛 stop the title bar region update crashing on startup ([cb176bb](https://github.com/hoobio/steward-companion/commit/cb176bbf81d7f3a60b337a7c0eac15868638a600))
* 🐛 use a release-notes icon for the changelog button ([64653c8](https://github.com/hoobio/steward-companion/commit/64653c8ba7e2bdcb5111395e77bd72ccb9e16804))

## [0.16.0](https://github.com/hoobio/steward-companion/compare/v0.15.0...v0.16.0) (2026-09-28)


### Features

* ✨ show Store update download and install progress ([b5c25ed](https://github.com/hoobio/steward-companion/commit/b5c25ed409684ae7738c39e7d14051a282250597))


### Bug Fixes

* 🐛 keep one instance of each page instead of rebuilding it per visit ([3a14412](https://github.com/hoobio/steward-companion/commit/3a14412a09b05b22e1c12f2808a72d4d5891606d))
* 🐛 stop the Addons table crashing when a filter removes rows ([50e90f0](https://github.com/hoobio/steward-companion/commit/50e90f0a74219281efa99f918312177d7c3d7252))

## [0.15.0](https://github.com/hoobio/steward-companion/compare/v0.14.0...v0.15.0) (2026-09-28)


### Features

* ✨ add a sortable Status column, last-updated Version sort and changelog ([bf2fa99](https://github.com/hoobio/steward-companion/commit/bf2fa99b25fb02a8df3eecc078354e1e96f1dfb3))
* ✨ add Write again to the Guides page ([4541745](https://github.com/hoobio/steward-companion/commit/4541745d28c133e3c97b6eda8e1506457ab381b5))
* ✨ carry recipe order, grey and colour bounds through the catalogue sync ([a9b4511](https://github.com/hoobio/steward-companion/commit/a9b4511e4a28d2286f8fec5b3d41ace008b3c8a8))
* ✨ decode addon TGA and BLP icons ([5fd577b](https://github.com/hoobio/steward-companion/commit/5fd577b22bb88aa0742d47cda236702b3b597a62))
* ✨ edit installs from Settings ([8378a24](https://github.com/hoobio/steward-companion/commit/8378a2449f70bd91c9805095159a10f522b2debc))
* ✨ install Store app updates from inside Steward ([80fd9a4](https://github.com/hoobio/steward-companion/commit/80fd9a48fdff477702d4a4cb5972cc406a28127f))
* ✨ let Status fill the table and tint the header row ([246ae40](https://github.com/hoobio/steward-companion/commit/246ae403227f5c6489005691f9f048cd221cab59))
* ✨ list only your own characters on the officer push row ([887def5](https://github.com/hoobio/steward-companion/commit/887def5b2f84f887817163e17a198a564aa76bfa))
* ✨ manage BugSack and BugGrabber under the addons flag ([13ae253](https://github.com/hoobio/steward-companion/commit/13ae2539a631287a2ef85a5369b770a38719f6c1))
* ✨ pick a stable initials colour per addon folder ([7191156](https://github.com/hoobio/steward-companion/commit/71911565ed79866848450097cde49254a1d3c38f))
* ✨ pick the install from the title bar for every page ([9b484d8](https://github.com/hoobio/steward-companion/commit/9b484d8649d4076c78586e9f85ac7d1bd4c4c922))
* ✨ read changelog notes from addon manifests ([2a9af26](https://github.com/hoobio/steward-companion/commit/2a9af26301874ca1223d3c16f5c771bfd22bacaf))
* ✨ read local addons, interface numbers and install overrides ([981336c](https://github.com/hoobio/steward-companion/commit/981336c28e12d460a1cba2eb261f9a6f0a79ffd7))
* ✨ replace a running Steward from another build and focus the same one ([3217a04](https://github.com/hoobio/steward-companion/commit/3217a04a850aee5258e3bf30fa89cc3f6d8349f9))
* ✨ resize Addons table columns ([133ce9f](https://github.com/hoobio/steward-companion/commit/133ce9f912c82b335f246734548ce782e1241161))
* ✨ show live updates as a ring around the avatar ([8c04c10](https://github.com/hoobio/steward-companion/commit/8c04c109893418fd67488e1eeb682131f7780772))
* ✨ show officer sync as two pulls and a push ([effd123](https://github.com/hoobio/steward-companion/commit/effd123596a605a87ad66e977225e3409f133d90))
* ✨ show the game version icon for an install ([6b84290](https://github.com/hoobio/steward-companion/commit/6b84290dcc2812fc8d8fcd77cfba6e3ce7346407))
* ✨ show the live-updates connection as a dot with a tooltip ([4aef4bb](https://github.com/hoobio/steward-companion/commit/4aef4bb4f5d9453146308d3a1593af60c9419d52))
* ✨ tell gigagrug the selected guild on a user-driven switch ([809e114](https://github.com/hoobio/steward-companion/commit/809e1142c55a3c7566eea795a2e21fd1dcf4f48e))
* ✨ turn the Addons page into a per-install addon manager ([add99f9](https://github.com/hoobio/steward-companion/commit/add99f9318c57cdd111d62f3655b003e8cdf77fa))


### Bug Fixes

* 🐛 apply Local rescans on the UI thread ([630d2b7](https://github.com/hoobio/steward-companion/commit/630d2b7fb4c6bd95d63aeb9b95b9d81ce27f2b0f))
* 🐛 centre a title-only banner and give local banners the info icon ([fd007f5](https://github.com/hoobio/steward-companion/commit/fd007f504c3ab7fa242f6847f42b49b16deb539a))
* 🐛 find your own characters by their Discord link ([6cb2804](https://github.com/hoobio/steward-companion/commit/6cb280415ee4429e1a5bf6fcd73ac32f3f937f68))
* 🐛 harden addon icon decoding ([846e6e1](https://github.com/hoobio/steward-companion/commit/846e6e19987c33d36c521c9c1f5ff1c42cb7dcc6))
* 🐛 keep a locked addon folder from breaking the Local scan ([7ec4618](https://github.com/hoobio/steward-companion/commit/7ec4618dd068023cb9c6aa163d84a0e833bbb86b))
* 🐛 keep Store self-update to one install at a time ([5a1fb76](https://github.com/hoobio/steward-companion/commit/5a1fb76b41220ee2b41de9113d675d5277ecf646))
* 🐛 keep the gap between the avatar ring and the name ([b8de0f6](https://github.com/hoobio/steward-companion/commit/b8de0f6c911153e3cb70cb43aad01ac41bf9d25b))
* 🐛 label a pre-release flight with the release it precedes ([f72f644](https://github.com/hoobio/steward-companion/commit/f72f6441b534308177a8d73bf432ac54d1ed0a53))
* 🐛 make Addons page row actions safe while busy and off the UI thread ([e858e83](https://github.com/hoobio/steward-companion/commit/e858e83a97447c491cd4ef9e31ad58cccc89236f))
* 🐛 match the toolbar-to-table gap to the title-to-toolbar gap ([d3fdb4f](https://github.com/hoobio/steward-companion/commit/d3fdb4fe71758306caa816a74cb4e4dc3166c233))
* 🐛 name each build's diagnostic log separately ([86f72bc](https://github.com/hoobio/steward-companion/commit/86f72bc07a7adc3eee1ffee9b5245b87f452a0f6))
* 🐛 open a title bar dropdown in one click while another is open ([0d82122](https://github.com/hoobio/steward-companion/commit/0d821226e4dfeb95f9f372db800993d2a533130f))
* 🐛 open flyouts on the first click into an inactive window ([cb179f8](https://github.com/hoobio/steward-companion/commit/cb179f8de0726136cd2846ec1dcdd3f96a16b26e))
* 🐛 place Write again below the install cards ([a3ac7d0](https://github.com/hoobio/steward-companion/commit/a3ac7d0e9d3291928f5b2ae308fa8f92a69ad96a))
* 🐛 place Write again under the guides list ([79be9e1](https://github.com/hoobio/steward-companion/commit/79be9e18405bf35730bf6cee6791de77d5f1f856))
* 🐛 push only professions to a server where the officer holds no seat ([50091b2](https://github.com/hoobio/steward-companion/commit/50091b252e248d5f1b89436d98bd0445ffb21815))
* 🐛 rewrite StewardGuides only when a guide or its script changes ([37da95e](https://github.com/hoobio/steward-companion/commit/37da95e5ea11dabf55e65d78d20f192c1e1da1ba))
* 🐛 run one Steward at a time across every build ([0d79c95](https://github.com/hoobio/steward-companion/commit/0d79c95a0968562ab311049fae7764b4f18631af))
* 🐛 send the pre-release version in the User-Agent ([ea11b18](https://github.com/hoobio/steward-companion/commit/ea11b18556faa1bc0ed498f225af1922f6b9845c))
* 🐛 show the last known update state on the About card ([48432af](https://github.com/hoobio/steward-companion/commit/48432afe55b4204b924099f79cc462afc91f4b10))
* 🐛 smooth the install picker and guild switcher open animation ([674122c](https://github.com/hoobio/steward-companion/commit/674122c0bd5a8f346c1537e76a9c33b3db814004))
* 🐛 spread RestedXP guide checks with random jitter ([9595e53](https://github.com/hoobio/steward-companion/commit/9595e537d4da851489561110e333a0b3c37c65b7))
* 🐛 tint flyout acrylic to the app palette ([fa14690](https://github.com/hoobio/steward-companion/commit/fa14690eee769dabb2c80029757af6c6255c4d0a))

## [0.14.0](https://github.com/hoobio/steward-companion/compare/v0.13.1...v0.14.0) (2026-09-27)


### Features

* ✨ move professions known recipes to schema-2 id arrays ([3052e9b](https://github.com/hoobio/steward-companion/commit/3052e9b817124d9f94aac209af3e8a20510c98ca))


### Bug Fixes

* 🐛 deserialise the officer recipe catalogue with gigagrug's snake_case keys ([2560551](https://github.com/hoobio/steward-companion/commit/256055177649812aebd736a1afb8cdd0ca5a5873))

## [0.13.1](https://github.com/hoobio/steward-companion/compare/v0.13.0...v0.13.1) (2026-09-27)


### Bug Fixes

* 🐛 restore the sync tick's status text on the officer Roster row ([d43c8e7](https://github.com/hoobio/steward-companion/commit/d43c8e71d3dfa63321c4386f1e9170a7872cdbd6))

## [0.13.0](https://github.com/hoobio/steward-companion/compare/v0.12.0...v0.13.0) (2026-09-27)


### Features

* ✨ add a Check for updates link to the sign-in gate footer ([38062cc](https://github.com/hoobio/steward-companion/commit/38062ccec9b380d5dd98dafcdda0fa4ce2e6c093))
* ✨ add a diagnostic file log for sign-in, sync and event-stream failures ([f861c83](https://github.com/hoobio/steward-companion/commit/f861c830990bd18c103f58cdd67a91bf42502cc7))
* ✨ add the guild roster and guild professions icons ([2458600](https://github.com/hoobio/steward-companion/commit/2458600ebd273aa0000a44c5a24e731de8366e12))
* ✨ add the hoobiscripts flag as its own grantable addon feature ([d7c8300](https://github.com/hoobio/steward-companion/commit/d7c8300b89c977c70231ca5c99c1e08f91c1b41c))
* ✨ cache addon icons on disk so rows paint at once on startup ([15a2b20](https://github.com/hoobio/steward-companion/commit/15a2b2029c1b8c08eb9f883b9a5a20f2d4e3d608))
* ✨ carry a me identity and a professions integrity fingerprint through StewardSync.lua ([140bec0](https://github.com/hoobio/steward-companion/commit/140bec03db7f0114e46bb4f2c74717cf91f34768))
* ✨ give a seatless sync holder a professions-only push ([6351480](https://github.com/hoobio/steward-companion/commit/63514804d2abe1cf8257aaf10aeab7a5d9d925bf))
* ✨ let a professions-only push add new guild catalogue recipes ([5d9d630](https://github.com/hoobio/steward-companion/commit/5d9d630d7b1d02a4ecf1c9e38b717ca69f18a548))
* ✨ list the characters a professions-only push covers on the Sync page ([9ff40a6](https://github.com/hoobio/steward-companion/commit/9ff40a65950069ca8bbe1bd7db23a5bf8737dbb7))
* ✨ move onto gigagrug's member-scoped API and sync the guild directory ([c86834b](https://github.com/hoobio/steward-companion/commit/c86834be6cbe5b06acebfadbc841cdc7f96f5a74))
* ✨ move to per-flag guild routes and gate steward/sync/roster/professions per guild ([a213246](https://github.com/hoobio/steward-companion/commit/a2132464c5f864969bf7f5390e8aee0c6c0e9c66))
* ✨ pull guild data the moment gigagrug reports a change and push character exports as soon as the game writes them ([10b1ec9](https://github.com/hoobio/steward-companion/commit/10b1ec9de3ae4ee49e9abf140a5876b692c70109))
* ✨ push only changed professions and show per-character sync state on the raider Sync page ([6725d4a](https://github.com/hoobio/steward-companion/commit/6725d4ae1c2b267c11d140341baefe9fe35caa03))
* ✨ refetch banners live on the access event stream's bannersChanged frame ([a6f7d7c](https://github.com/hoobio/steward-companion/commit/a6f7d7ce5ffac737c9aac07e30c673d03b63ec9c))
* ✨ send a Steward User-Agent on every gigagrug request ([28a83f1](https://github.com/hoobio/steward-companion/commit/28a83f1ef15507473c7e1f9d11f06d2808d3ecdb))
* ✨ show a green tick on the Sync page roster row when it is in sync with the guild ([10b1ec9](https://github.com/hoobio/steward-companion/commit/10b1ec9de3ae4ee49e9abf140a5876b692c70109))
* ✨ show a live-updates indicator in the title bar ([adb2596](https://github.com/hoobio/steward-companion/commit/adb2596ee504aa823924ef480adc9ee8c6fabb28))
* ✨ show guild roster, guild professions and your characters rows on the raider Sync page ([00ee5a7](https://github.com/hoobio/steward-companion/commit/00ee5a72fc23aa8cfa0657e826212e9fd43b680b))
* ✨ show server-driven banners from gigagrug on every page ([30b76fb](https://github.com/hoobio/steward-companion/commit/30b76fb4aebae2e7bbef03d8b7cd57d37cf966f7))
* ✨ stream access changes to every signed-in user, not just officers ([85a947c](https://github.com/hoobio/steward-companion/commit/85a947ce8ae4f8b001ef7f6d448bf0fc3e2b7ac5))
* ✨ write pinned character links into StewardSync.lua ([6dab464](https://github.com/hoobio/steward-companion/commit/6dab464e4040a5e6b2a31fff05585a01b16278f2))


### Bug Fixes

* 🐛 add a link and copy fallback to the Discord sign-in wait screen ([a6cf296](https://github.com/hoobio/steward-companion/commit/a6cf2969771f6bd2fba73d4f611a7912f2053ef9))
* 🐛 align raider Sync row text to the card's right edge ([0d4715b](https://github.com/hoobio/steward-companion/commit/0d4715ba8a4d28543ec532aba638ba2f5c43abdd))
* 🐛 do not record a replayed professions batch as every character synced ([a0399dd](https://github.com/hoobio/steward-companion/commit/a0399ddc4aa0c989221bfa66853d937fe2eacd28))
* 🐛 drop the duplicate timestamp lines from officer Sync dataset rows ([c8731a7](https://github.com/hoobio/steward-companion/commit/c8731a74294743af37c6bb82f81c9b8873e00a9c))
* 🐛 forward a reagent without a name so the catalogue fingerprint matches ([6b55768](https://github.com/hoobio/steward-companion/commit/6b557683152f1a2d3b24fd6f09295fce7548f9f5))
* 🐛 give a throttled character-sync push its own retry message ([1f87313](https://github.com/hoobio/steward-companion/commit/1f87313e4f56bf6ee271721c49c8e95338febd72))
* 🐛 hide the WoW-running banner on the Sync page once data is exported after the current session ([10b1ec9](https://github.com/hoobio/steward-companion/commit/10b1ec9de3ae4ee49e9abf140a5876b692c70109))
* 🐛 keep a completed Discord sign-in and show sign-in errors on the gate ([9000b1b](https://github.com/hoobio/steward-companion/commit/9000b1b5334a07ff8c39a1bd18166125ef016561))
* 🐛 keep rejected characters reading as not accepted on the raider Sync page ([6214348](https://github.com/hoobio/steward-companion/commit/6214348076eefc7b4028b9becc118f4de8c4c5f3))
* 🐛 make Write again pull and rewrite the guild data file for every user ([5f8c95a](https://github.com/hoobio/steward-companion/commit/5f8c95a2d8c27d4e8df0afe49087806389500811))
* 🐛 open the Microsoft Store updates page from Check for a new version ([10b1ec9](https://github.com/hoobio/steward-companion/commit/10b1ec9de3ae4ee49e9abf140a5876b692c70109))
* 🐛 rewrite StewardSync.lua when the file on disk is not the one last written ([4fac5c4](https://github.com/hoobio/steward-companion/commit/4fac5c43d129a1b8e024fa6085f64c90518a7c27))
* 🐛 show per-character sync outcomes on the raider Sync page ([8592439](https://github.com/hoobio/steward-companion/commit/8592439f77fb03ff5cbef8180d40787079283685))
* 🐛 show the Addons page banner only when updates are available ([10b1ec9](https://github.com/hoobio/steward-companion/commit/10b1ec9de3ae4ee49e9abf140a5876b692c70109))
* 🐛 use the Forever professions portrait for the Sync professions tile ([e2dd556](https://github.com/hoobio/steward-companion/commit/e2dd5561f19cb5e2fd3e55395f6f3791bedb000d))
* 🐛 write character links as a top-level StewardSync.lua key ([f7a1ccd](https://github.com/hoobio/steward-companion/commit/f7a1ccd7e92035c5add508210b1de45d14a9c869))

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
