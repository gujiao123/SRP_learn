---
name: project-graph-knowledge-map
description: "Design and generate high-quality knowledge graphs for Project Graph (.prg files). Use when building learning maps, concept graphs, or tutorial visualizations for the Project Graph application."
---

# Project Graph 知识图谱设计 Skill

## 📌 工具与环境

- **文件格式**：`.prg` = ZIP 压缩包（`stage.msgpack` 存储所有节点和连线）
- **操作脚本**：`e:\unityProject\SRP_started\Assets\prg_editor.py`
- **Python 解释器**：`F:\MyVCP\newVCP\VCPServer\src\tools\extern\.venv\Scripts\python.exe`
- **运行方式**：`uv run --python <解释器路径> --with msgpack <脚本>`
- **格式参考**：`e:\unityProject\SRP_started\Assets\教程.prg`（官方教程图谱）

---

## 🏗️ .prg 数据格式（旧格式 list 结构）

`stage.msgpack` 是一个 Python **list**，混合存放节点和连线：

### TextNode（文本节点）
```python
{
    "_": "TextNode",
    "uuid": str,                    # UUID
    "text": str,                    # 节点内容（支持换行 \n）
    "collisionBox": {
        "_": "CollisionBox",
        "shapes": [{
            "_": "Rectangle",
            "location": {"_": "Vector", "x": float, "y": float},
            "size": {"_": "Vector", "x": float, "y": float},
        }]
    },
    "color": {"_": "Color", "r": int, "g": int, "b": int, "a": float},
    "fontScaleLevel": 0,
    "sizeAdjust": "auto",           # "auto" 或 "manual"
    "details": [],
}
```

### LineEdge（有向连线）
```python
{
    "_": "LineEdge",
    "uuid": str,
    "text": str,                    # 连线上的标签文字
    "color": {"_": "Color", "r": 0, "g": 0, "b": 0, "a": 0},
    "lineType": "solid",
    "associationList": [
        {"$": "/N"},                # N = 源节点在 stage list 中的 index
        {"$": "/M"},                # M = 目标节点在 stage list 中的 index
    ],
    "sourceRectangleRate": {"_": "Vector", "x": 0.5, "y": 0.5},
    "targetRectangleRate": {"_": "Vector", "x": 0.5, "y": 0.5},
}
```

> ⚠️ **关键**：`associationList` 中的 `$` 是 JSON Pointer，引用的是 `stage` 列表的**数组下标**，不是 UUID！

### 颜色规范
```python
透明（默认）: {"_": "Color", "r": 0, "g": 0, "b": 0, "a": 0}
白色:         {"_": "Color", "r": 255, "g": 255, "b": 255, "a": 1}
黄色（主线）: {"_": "Color", "r": 234, "g": 179, "b": 8, "a": 1}
蓝色（概念）: {"_": "Color", "r": 59, "g": 130, "b": 246, "a": 1}
绿色（完成）: {"_": "Color", "r": 22, "g": 163, "b": 74, "a": 1}
红色（警告）: {"_": "Color", "r": 239, "g": 68, "b": 68, "a": 1}
紫色（扩展）: {"_": "Color", "r": 139, "g": 92, "b": 246, "a": 1}
```

---

## 🎨 知识图谱设计原则

从官方教程图谱（939个对象）中提炼的最佳实践：

### 1. 节点命名原则

| 类型 | 命名风格 | 示例 |
|------|---------|------|
| **操作节点** | 动词短语 | `创建连线`、`移动节点`、`删除物体` |
| **概念节点** | 名词 + 说明 | `实体 / 能够独立存在的东西` |
| **步骤节点** | 数字 + 动作 | `1：先框选所有节点`、`2：再按Ctrl` |
| **快捷键节点** | 直接写键位 | `Ctrl+F`、`右键拖拽` |
| **结果节点** | 描述现象 | `会导致视野移动` |

### 2. 连线标签原则

```
src --[是]--> dst          # 定义关系
src --[分为]--> dst        # 分类关系
src --[解决]--> dst        # 因果/解决关系
src --[导致]--> dst        # 因果/问题关系
src --[方法1]--> dst       # 多种方法并列
src --[依赖]--> dst        # 依赖关系
src --[包含]--> dst        # 包含关系
src --[goto]--> dst        # 跳转/参考
```

### 3. 颜色使用规范

- 🟡 **黄色**：主线学习路径
- 🔵 **蓝色**：核心概念节点
- 🟢 **绿色**：已完成/正面结果
- 🔴 **红色**：问题/错误/警告
- 🟣 **紫色**：扩展/补充知识
- ⬜ **透明**：普通节点（默认）

### 4. 布局原则

```
主题节点（中心）
├── 概念一（左侧，X 偏移 -300）
│   ├── 子概念（X 偏移 -600，Y 递增 110）
│   └── 子概念
├── 概念二（上方，Y 偏移 -300）
└── 概念三（右侧，X 偏移 +300）
```

- **列间距**：250~350px
- **行间距**：90~130px（每个节点约 76px 高）
- **各主题分组要有足够间距**，不同区域至少间隔 400px

### 📐 节点实际尺寸参考（实测数据）

**Y轴（高度）**
| 文字行数 | 节点高度 |
|---------|---------|
| 1行 | ~100px |
| 2行 | ~150px |
| 6行 | ~350px |
| 9行 | ~500px |

> 规律：首行 100px，每增加一行约 +50px

**X轴（宽度）**
| 内容 | 宽度 |
|------|------|
| 16个中文字（一行最宽） | ~550px |
| 12个中文字 | ~450px |
| 9个中文字 | ~300px |

> 规律：每个中文字约 34px 宽

**布局间距建议**
- 节点间纵向间距（`YS`）= 节点高度 + ≥100px gap
  - 单行节点之间: YS ≈ 200px
  - 6行节点之间: YS ≈ 450~500px
  - 9行节点之间: YS ≈ 600~650px
- 节点间横向间距（`XS`）= 节点宽度 + ≥150px gap
  - 标准列间距: XS ≈ 700~800px（宽节点）
  - 紧凑列间距: XS ≈ 500px（短节点）


### 5. 节点内容深度

对于学习型图谱，节点内容应该分层：

```
# 浅层（标题节点）
Shadow Acne

# 中层（定义+原因）
Shadow Acne（自遮蔽伪影）
原因：分辨率不足，表面遮挡自身

# 深层（完整知识卡片）
Shadow Acne（自遮蔽伪影）
━━━━━━━━━━━━
现象：表面出现条纹状阴影
原因：Shadow Map 分辨率有限
      同一像素覆盖多个片元
解决：Normal Bias / Slope Bias
```

---

## 🛠️ 生成图谱的代码模板

```python
"""生成 XX 知识图谱"""
import sys
sys.path.insert(0, r'e:\unityProject\SRP_started\Assets')
from prg_editor import PrgFile

PRG = r'e:\unityProject\SRP_started\Assets\目标文件.prg'
f = PrgFile(PRG)

# ── 布局基准 ──
BX, BY = 0, 0        # 整体起始坐标
XS, YS = 300, 110    # 列间距、行间距

def add(text, x, y, color=None):
    return f.add_node(text, BX + x, BY + y, color)

def link(a, b, label=""):
    return f.add_edge(a, b, label)

# 颜色常量
YELLOW = [234, 179, 8, 1]
BLUE   = [59, 130, 246, 1]
GREEN  = [22, 163, 74, 1]
RED    = [239, 68, 68, 1]
PURPLE = [139, 92, 246, 1]

# ── 生成图谱 ──
root = add("📖 主题节点", 0, 0, BLUE)

g1 = add("概念一", -XS, YS * 2)
link(root, g1, "包含")

n1 = add("子概念\n详细说明", -XS * 1.5, YS * 3.5)
link(g1, n1, "是")

# ... 更多节点

f.save()
print("✅ 图谱生成完成！")
```

---

## 📋 生成流程（Step by Step）

### Step 1：分析内容结构
1. 列出所有核心概念
2. 找出概念之间的关系（因果/包含/依赖/并列）
3. 规划主题分组（建议 4~6 个主要分支）
4. 确定主线学习路径（用黄色标注）

### Step 2：设计节点内容
- 每个节点只表达**一个核心思想**（原子化）
- 重要节点加上"是什么 + 为什么"的说明
- 快捷键/代码用专门节点，不混入概念节点

### Step 3：规划布局坐标
```
主题根节点：(0, 0)
左侧分组：  (-BX, BY)
右侧分组：  (+BX, BY)
上方分组：  (0, -BY)
下方分组：  (0, +BY)
子节点：    父节点坐标 ± (XS, YS)
```

### Step 4：生成 .prg 文件
- 使用 `prg_editor.py` 中的 `PrgFile` 类
- 先 `add_node` 获取 index，再 `add_edge` 连线
- 调用 `f.save()` 写入文件

### Step 5：验证
- 在 Project Graph 中打开文件
- 检查节点间距是否合适
- 核实连线方向和标签是否正确

---

## ⚠️ 常见错误

| 错误 | 原因 | 解决 |
|------|------|------|
| `Cannot read properties of null (reading 'uuid')` | 连线引用了错误的 index | 确保 `$:/N` 中 N 是正确的 stage 数组下标 |
| 节点重叠 | 坐标计算错误 | 加大 XS/YS 间距，或手动调整 BX/BY |
| 连线很乱 | 所有节点在同一位置 | 检查坐标是否正确传入 `add_node` |
| 文件无法打开 | msgpack 格式错误 | 从 `.bak` 备份恢复，检查 Python 代码 |

---

## 🔧 prg_editor.py 核心 API

```python
f = PrgFile(path)           # 打开文件
idx = f.add_node(text, x, y, color=None)  # 添加节点，返回 index
f.add_edge(src_idx, dst_idx, label="")    # 添加连线
f.clear()                   # 清空所有内容
f.info()                    # 打印当前内容摘要
f.save(backup=True)         # 保存（自动备份到 .bak）

# 树形生成（对应官方 generate_node_tree_by_text）
f.generate_tree(tree_text, start_x=0, start_y=0, x_spacing=250, y_spacing=90)
```
