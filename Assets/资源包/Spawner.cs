using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawner : MonoBehaviour
{

    public static Spawner Instance; 
    
    public GameObject enemy1; //敌人1的预制体
    public GameObject enemy2; //敌人2的预制体

    public List<Transform> spawnPosList1 = new List<Transform>();
    public List<Transform> spawnPosList2 = new List<Transform>();

    public int remainEnemies = 4; //剩余敌人数量
    public bool isSpawning = true; //生成器的状态

    private void Awake()
    {
        Instance = this;
        enemy1 = Resources.Load<GameObject>("Prefab/Enemy1");
        enemy2 = Resources.Load<GameObject>("Prefab/Enemy2");
    }

    // Start is called before the first frame update
    void Start()
    {
        SpawnEnemy(spawnPosList1, enemy1);
    }

    // Update is called once per frame
    void Update()
    {
        //1层敌人死光了
        if (remainEnemies == 2 && isSpawning)
        {
            //开启栅栏
            Gate.Instance.OpenGate();
            
            //生成第二波敌人
            SpawnEnemy(spawnPosList2, enemy2);
            
            //关闭生成器
            isSpawning = false;


        }

        //游戏胜利
        if (remainEnemies == 0 && !GameManager.Instance.isWin )
        {
            Debug.Log("游戏胜利");
            GameManager.Instance.Win();
        }
        
        
    }

    //生成敌人
    public void SpawnEnemy(List<Transform> spawnList, GameObject enemyType)
    {
        foreach (var pos in spawnList)
        {
            Instantiate(enemyType, pos);
            
        }
    }
 }
