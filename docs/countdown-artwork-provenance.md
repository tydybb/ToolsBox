# 下班搭子素材来源与制作说明

## 原作参考

- 原角色作者：[暹罗厘普](https://www.weibo.com/u/7746558637)。
- 本次核对的作者公开表情帖：[2024-10-25 是 xllp 表情包](https://www.sina.cn/news/detail/5093458736059295.html)。公开页九张图为 240×240 静态 JPG，本次未找到可确认且适用的官方原版 GIF。读取时仅使用公开网页与普通页面 Referer，无登录 Cookie、账号或本地聊天数据。
- 秀珍参考：`https://wx3.sinaimg.cn/middle/008sfLM9ly1huywq0h5ahj306o06ojrl.jpg`。
- 小河参考：`https://wx2.sinaimg.cn/middle/008sfLM9ly1huywq0mpolj306o06o0sw.jpg`。
- 多栋参考：`https://wx4.sinaimg.cn/middle/008sfLM9ly1huywq0a2r6j306o06oq33.jpg`。
- 田中参考：`https://wx3.sinaimg.cn/middle/008sfLM9ly1huywq1qs9mj306o06oq3b.jpg`（原图被多栋抱住）。
- 厘普参考：`https://wx4.sinaimg.cn/middle/008sfLM9ly1huywq1h6h3j306o06odg2.jpg`。
- 五角色比例补充参考：`https://image.woshipm.com/2025/02/27/23c52df6-f520-11ef-8a16-00163e09d72f.png`，第三方文章收录，不作为官方授权证据。

## 最终项目素材

`src/ToolsBox.App/Assets/Countdown/` 内五张 PNG 为使用内置 imagegen 工具按以上原画参考新绘制的帧图集，不是从原版 GIF 解包取得，不表示官方授权或联名。用户已明确要求原版动图无法收集时按原画风补绘。公开可见不等于作者许可随软件分发；本说明记录来源与制作方式，不授予原角色权利。若公开发行，请核实所需的素材使用许可。

| 文件 | 角色 | 布局 |
| --- | --- | --- |
| lipu.png | 暹罗厘普 | 1536×1024，6 列×4 行 |
| duodong.png | 比格多栋 | 1536×1024，6 列×4 行 |
| xiuzhen.png | 兔子秀珍 | 1536×1024，6 列×4 行 |
| tianzhong.png | 张田中 | 1536×1024，6 列×4 行 |
| xiaohe.png | 鹈鹕小河 | 1536×1024，6 列×4 行 |

名义网格为 256×256。行顺序：平静 calm、疲惫 tired、兴奋 happy、崩溃 stressed；列为每组动画的六个顺序帧。生成图集的绘制内容未必严格落在名义网格内，运行时按实际透明间隙切分，并使用整张图统一缩放和脚底基线，避免跨格残留。背景具有真实 Alpha 透明度，显示时无底色贴片。角色、眼睛、嘴型或手势随帧变化，不是静态图的位移。WPF 播放间隔 160 ms。

## 生成提示词

五张图共享提示词：

```text
Use case: identity-preserve. Asset: production WPF 2D reaction animation sprite sheet.
References are original 暹罗厘普 illustrations ONLY for character identity and drawing style.
Re-draw this ONE character in exactly 6 COLUMNS and 4 ROWS, 24 equally sized square cells,
preferred canvas 1536x1024 pixels, each cell 256x256. Transparent RGBA canvas,
NO white background, NO checkerboard, NO grid lines, NO labels, NO text, NO watermark.
Preserve the original flat scribbly hand-drawn Chinese meme look: slightly wobbly thick
black outline, simple flat colors, irregular asymmetrical exaggerated facial features;
no 3D, no painterly shading, no plush, no anime redesign. Entire body comfortably inside
each cell with same center, scale, and foot baseline in all 24 cells; 15% transparent
safety margins; no limbs crossing into neighbors. Each row is a coherent 6-frame looping
animation with clearly changing FACIAL EXPRESSION and HAND GESTURE, not 6 copies of one
image or just a translated static drawing.
Row 1 calm: eyes open, half closed, closed blink, half closed, eyes open and tiny wave, return.
Row 2 tired: droopy eyes, slow blink, deeper sigh/open mouth, tiny tear, wiping tear,
droopy eyes return.
Row 3 happy: grin, paws rising, arms raised cheer, clapping, arms lowering, grin return.
Row 4 stressed: anxious eyes, hands to face, trembling clenched hands, frustrated open mouth,
hands lowering, anxious eyes return. Keep poses readable at 160px.
Remove all writing and background of references.
```

各角色追加提示词：

- **lipu**：Only 暹罗厘普 the Siamese cat (leftmost in lineup). Cream beige short compact cat, dark warm-gray taupe face mask and inner pointed ears, giant exaggerated WHITE eye shapes with irregular black pupils, simple whisker marks, small black mouth, slender curved tail. Do NOT copy the exposed brain from other memes. No other animals.
- **duodong**：Only 比格多栋 the beagle dog (second in lineup). Golden tan head and LONG floppy tan ears, distinct broad WHITE vertical forehead blaze and white muzzle/belly/body, short stocky body, black oval nose, huge irregular white eyes with small black pupils, black line mouth. Maintain original slightly derpy odd-looking dog, not a conventional cute puppy. No other animals.
- **xiuzhen**：Only 兔子秀珍 pink rabbit (middle of lineup). Solid bubblegum pink rounded body and head, two long upright ears with pale cream yellow interiors, huge almost entirely BLACK ROUND EYES with large WHITE glints, tiny pink oval nose and cream two-lobed muzzle. Preserve original unusual asymmetrical black eyes/muzzle. No other animals.
- **tianzhong**：Only 张田中 the carrot (fourth in lineup), not the dog holding him. Long tapered saturated ORANGE carrot body, three-lobed vibrant GREEN leaf crown, tiny BLACK stick-line arms and legs, extremely minimal tiny eyes drawn as dark dots/short slanted strokes and broad expressive black line mouth. No white eyeballs, no furry body, no other characters. Keep the distinctive dark-humor world-weary vegetable face.
- **xiaohe**：Only 鹈鹕小河 the white pelican (rightmost in lineup). Plump WHITE bird body and small white head, long VERY WIDE flattened PALE YELLOW beak with a big yellow lower throat pouch, small black dot eyes, black outlined wings acting as hands, two yellow webbed feet. No human hand patting its head, no sign, no other characters.

## 高清图标

锤子品牌图沿用项目已有 `Assets/baoge-toolbox-icon.png`（1254×1254），本次让 WPF 直接加载高清 PNG 并使用 HighQuality 缩放，避免此前实际读取 ICO 的 16 像素帧。多尺寸 `baoge-toolbox.ico` 继续供 EXE / 原生任务栏 / 托盘使用（16、32、48、64、128、256 像素）。本次没有重新声称旧图为新生成素材。

## 2026-10-08 走路补帧

新增 `lipu-walk.png`、`duodong-walk.png`、`xiuzhen-walk.png`、`tianzhong-walk.png`、`xiaohe-walk.png`，分别以对应既有表情图集作为 imagegen 的身份与画风参考重新绘制，未覆盖原表情素材。五张图均为 1536×1024、6 列×4 行的透明 PNG；行仍按 calm、tired、happy、stressed 顺序。每行六列为交替迈步姿态，面朝右；WPF 往左走时只镜像角色图片，姓名不镜像。停下改回原表情图集。新增共 120 帧，总计 240 帧。仍为非官方补绘，来源及权利说明同上。

共同提示词（每次只生成一个角色，附对应原图）：

```text
Create a transparent PNG animation sprite sheet for a native WPF desktop character.
Use the attached existing expression sheet as the exact identity, proportions, palette,
and rough hand-drawn black-outline meme style reference, not as a layout to copy.
Output EXACTLY 1536x1024 pixels, a perfect 6 columns by 4 rows grid, each cell 256x256.
No grid lines, text, labels, watermark, scenery, ground, checkerboard or solid background.
Every cell transparent with generous clear margins. Every complete character same size,
center and foot baseline. All face RIGHT in slight side/three-quarter view, face recognizable.
Six DIFFERENT walking gait phases: right foot forward contact, down, passing, left foot
forward contact, down, passing; seamless loop. Alternate legs visibly, swing arms/tail;
do NOT just translate a still pose. Keep head/body stable; never crop ears/tail/feet.
Four rows repeat the walk with expressions: calm neutral, tired droopy, happy excited,
stressed worried. Preserve identity, scale, orientation, baseline. Bold irregular black
ink and flat original colors; no redesign. Nonofficial derivative walking animation.
Preserve genuinely transparent alpha.
```

角色限定：厘普为奶油色暹罗猫、灰褐面罩、夸张白眼、卷尾及小短腿；多栋为棕色长垂耳比格、白额纹与白身、黑鼻；秀珍为粉色长耳兔、奶油色内耳与双瓣嘴、黑眼白高光及小短脚；田中为橙色锥形胡萝卜、三片绿叶、黑色线条手脚与点眼；小河为白鹈鹕、大扁浅黄嘴囊、点眼、短翼及黄蹼脚。

## 2026-10-08 到点提醒图片

用户提供本地“暹罗厘普”图片目录作为只读参考。使用 imagegen 逐图整理清晰线条和透明背景，未覆盖、删除原文件；五张成图实际为 1254×1254 RGBA PNG，显示时统一缩放到 220 像素。它们是单张插图，不按动画图集切分。运行时全部使用内置资源，无须读取用户图片目录；不表示官方授权或联名，发行许可说明同上。

| 成图 | 本地参考 |
| --- | --- |
| offwork-ride.png | 20230501233950_f60df.thumb.1000_0.jpg |
| offwork-rest.png | 20230501234308_1ed20.thumb.1000_0.jpg |
| offwork-drool.png | AvSobzaBiw0lJ5V.thumb.1000_0.jpg |
| offwork-run.png | wgSDw8qyf9ew9lg.thumb.1000_0.jpg |
| offwork-beagle.png | 1c068a80bbb88febeb55f8a3459807e13cc4744ca384-8C2p1w_fw240webp.webp |

按实际日期确定五天一组的伪随机顺序，组内各图只出现一次，跨组相邻日期也不重复；同一天重开或重复提醒保持同图。不是每次重开重新随机。

### 本轮逐图实际提示词

#### ride

```text
Use case: identity-preserve and background-extraction. Asset: a single high-resolution transparent meme illustration for a WPF off-work reminder, displayed around 220px. Image 1 is the edit target and exact character/style/pose reference. Primary request: improve clarity and reconstruct clean linework, isolate the illustrated subject on true transparent alpha; retain the existing composition and original funny hand-drawn flat meme aesthetic. Subject: the beige Siamese meme cat riding on the white pelican, with its large pale yellow beak and yellow feet; keep both poses, deadpan faces and the sense of leaving work. Output 1024x1024 PNG, character group centered with 10% transparent safety margins, complete important ears/tails/feet/props, no clipping. Preserve the original colors, black uneven ink outlines, asymmetrical eyes, mouth shape, body proportions, poses and expressions. Background genuinely transparent, including the former white negative spaces outside the white characters; do not erase white pelican/dog bodies. No extra characters, no added decorative frame or solid background, no 3D, no shiny anime repaint, no visible explanatory text or UI labels. This is a nonofficial clarity-restored derivative, not a redesign.
```

#### rest

```text
Use case: identity-preserve and background-extraction. Asset: a single high-resolution transparent meme illustration for a WPF off-work reminder, displayed around 220px. Image 1 is the edit target and exact character/style/pose reference. Primary request: improve clarity and reconstruct clean linework, isolate the illustrated subject on true transparent alpha; retain the existing composition and original funny hand-drawn flat meme aesthetic. Subject: the exhausted orange carrot with green leaves lying on a grey pillow and grey mat, tangled black frustration scribble above its head; preserve mat, pillow, scribble and world-weary expression. Output 1024x1024 PNG, character group centered with 10% transparent safety margins, complete important ears/tails/feet/props, no clipping. Preserve the original colors, black uneven ink outlines, asymmetrical eyes, mouth shape, body proportions, poses and expressions. Background genuinely transparent, including the former white negative spaces outside the white characters; do not erase white pelican/dog bodies. No extra characters, no added decorative frame or solid background, no 3D, no shiny anime repaint, no visible explanatory text or UI labels. This is a nonofficial clarity-restored derivative, not a redesign.
```

#### drool

```text
Use case: identity-preserve and background-extraction. Asset: a single high-resolution transparent meme illustration for a WPF off-work reminder, displayed around 220px. Image 1 is the edit target and exact character/style/pose reference. Primary request: improve clarity and reconstruct clean linework, isolate the illustrated subject on true transparent alpha; retain the existing composition and original funny hand-drawn flat meme aesthetic. Subject: the tired beige Siamese meme cat collapsed beside a computer with half-closed exaggerated eyes and blue drool puddle; keep the exhausted absurd face and short black whiskers, complete the composition safely inside the canvas. Output 1024x1024 PNG, character group centered with 10% transparent safety margins, complete important ears/tails/feet/props, no clipping. Preserve the original colors, black uneven ink outlines, asymmetrical eyes, mouth shape, body proportions, poses and expressions. Background genuinely transparent, including the former white negative spaces outside the white characters; do not erase white pelican/dog bodies. No extra characters, no added decorative frame or solid background, no 3D, no shiny anime repaint, no visible explanatory text or UI labels. This is a nonofficial clarity-restored derivative, not a redesign.
```

#### run

```text
Use case: identity-preserve and background-extraction. Asset: a single high-resolution transparent meme illustration for a WPF off-work reminder, displayed around 220px. Image 1 is the edit target and exact character/style/pose reference. Primary request: improve clarity and reconstruct clean linework, isolate the illustrated subject on true transparent alpha; retain the existing composition and original funny hand-drawn flat meme aesthetic. Subject: the tan beagle with white forehead blaze running, beige Siamese cat and orange green-leaf carrot riding on its back; three characters total, preserve their funny tired expressions and running pose; replace blurry edges with sharp irregular black ink, do not redesign them. Output 1024x1024 PNG, character group centered with 10% transparent safety margins, complete important ears/tails/feet/props, no clipping. Preserve the original colors, black uneven ink outlines, asymmetrical eyes, mouth shape, body proportions, poses and expressions. Background genuinely transparent, including the former white negative spaces outside the white characters; do not erase white pelican/dog bodies. No extra characters, no added decorative frame or solid background, no 3D, no shiny anime repaint, no visible explanatory text or UI labels. This is a nonofficial clarity-restored derivative, not a redesign.
```

#### beagle

```text
Use case: identity-preserve and background-extraction. Asset: a single high-resolution transparent meme illustration for a WPF off-work reminder, displayed around 220px. Image 1 is the edit target and exact character/style/pose reference. Primary request: improve clarity and reconstruct clean linework, isolate the illustrated subject on true transparent alpha; retain the existing composition and original funny hand-drawn flat meme aesthetic. Subject: the deadpan tan beagle with white blaze and long floppy ears resting at a keyboard and computer, with tiny tear-like strokes; faithfully preserve sleepy face, odd proportions and original flat meme style; improve the low-resolution pixelated edges with clean irregular black handdrawn strokes. Output 1024x1024 PNG, character group centered with 10% transparent safety margins, complete important ears/tails/feet/props, no clipping. Preserve the original colors, black uneven ink outlines, asymmetrical eyes, mouth shape, body proportions, poses and expressions. Background genuinely transparent, including the former white negative spaces outside the white characters; do not erase white pelican/dog bodies. No extra characters, no added decorative frame or solid background, no 3D, no shiny anime repaint, no visible explanatory text or UI labels. This is a nonofficial clarity-restored derivative, not a redesign.
```
