using UnityEngine;

// me14: 标记某个 int 字段是"渲染层遮罩"，让 Inspector 显示成下拉菜单（而不是普通 int 输入框）
// 配合 RenderingLayerMaskDrawer（Editor 下的 PropertyDrawer）使用
public class RenderingLayerMaskFieldAttribute : PropertyAttribute { }
