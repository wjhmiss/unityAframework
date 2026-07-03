using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public class PlayerVFXManager : MonoBehaviour
{

    public VisualEffect footStep; //移动
    public VisualEffect heal; //回血特效
    public ParticleSystem blade01; //攻击特效1
    public ParticleSystem blade02; //攻击特效2
    public ParticleSystem blade03; //攻击特效3

    public bool isPlayingFoot = false; // 当前是否正在播放动画
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

        
        // 异或 两个相同则为false, 两者不同则为true
        // 没移动, 不执行
        // 刚开始移动,  执行一次if, 启动特效, 标记isPlayingFoot为真
        // 移动后, if不再执行, 一直播放特效
        // 停止移动前,执行if, 特效停止,  标记isPlayingFoot为假
        // 停止移动后, if不执行
        if (isPlayingFoot ^ Player.Instance.isMove)
        {
            Update_FootStp(Player.Instance.isMove);
            isPlayingFoot = !isPlayingFoot;
        }
        
    }
    
    //脚步特效
    private void Update_FootStp(bool state)
    {
        if (state)
        {
            footStep.Play(); 
        }
        else
        {
            footStep.Stop();
        }
    }
    
    //攻击1连击特效
    public void PlayAttackVFX01()
    {
        blade01.Play();
    }
    
    //攻击2连击特效
    public void PlayAttackVFX02()
    {
        blade02.Play();
    }
    
    //攻击3连击特效
    public void PlayAttackVFX03()
    {
        blade03.Play();
    }
}
