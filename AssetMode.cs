using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System.Collections.Generic;
using Terraria.Audio;
using Terraria.Graphics.Shaders;

namespace InnoVault
{
    /// <summary>
    /// 资源模式，将要加载什么样的资源
    /// </summary>
    public enum AssetMode
    {
        /// <summary>
        /// 无类型，目前用于自动指定
        /// </summary>
        None,
        /// <summary>
        /// 声音，即<see cref="SoundStyle"/>类型
        /// </summary>
        Sound,
        /// <summary>
        /// 加载<see cref="Asset{T}"/>纹理类文件
        /// </summary>
        Texture,
        /// <summary>
        /// 加载<see cref="Asset{T}"/>渲染类文件
        /// </summary>
        Effects,
        /// <summary>
        /// 加载<see cref="ArmorShaderData"/>类型的渲染类文件
        /// </summary>
        ArmorShader,
        /// <summary>
        /// 加载<see cref="MiscShaderData"/>类型的渲染类文件
        /// </summary>
        MiscShader,
        /// <summary>
        /// 加载<see cref="Texture2D"/>纹理类文件
        /// </summary>
        TextureValue,
        /// <summary>
        /// 加载<see cref="Effect"/>类型的渲染类文件
        /// </summary>
        EffectValue,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="SoundStyle"/>
        /// </summary>
        SoundArray,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="Asset{T}"/>，其中 T 为 <see cref="Texture2D"/>
        /// </summary>
        TextureArray,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="Asset{T}"/>，其中 T 为 <see cref="Effect"/>
        /// </summary>
        EffectArray,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="ArmorShaderData"/>
        /// </summary>
        ArmorShaderArray,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="MiscShaderData"/>
        /// </summary>
        MiscShaderArray,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="Texture2D"/>
        /// </summary>
        TextureValueArray,
        /// <summary>
        /// 加载数组批次处理，用于<see cref="IList{T}"/>，其中 T 为 <see cref="Effect"/>
        /// </summary>
        EffectValueArray,
        /// <summary>
        /// 自定义资源类型，由<see cref="VaultLoadenHandle"/>的派生类处理
        /// </summary>
        Custom,
        /// <summary>
        /// 自定义资源数组类型，由<see cref="VaultLoadenHandle"/>的派生类处理
        /// </summary>
        CustomArray,
    }
}
