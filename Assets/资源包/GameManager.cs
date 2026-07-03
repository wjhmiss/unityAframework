using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    
    public GameObject PausePanel;
    public GameObject GameOverPanel;
    public GameObject GameWinPanel;

    public Button ResumeButton; //继续按钮
    public Button MainMenuButton; //主菜单按钮
    public Button RestartButton; //重新开始按钮
    public Button QuitButton; //退出按钮

    public bool isWin = false;
    public bool isFail = false;
    
    
    //玩家游戏信息
    public Slider _healthSlider;
    public TMP_Text _coinText;
    

    private void Awake()
    {
        Instance = this; 
        PausePanel = GameObject.Find("PausePanel");
        GameOverPanel = GameObject.Find("GameOverPanel");
        GameWinPanel = GameObject.Find("GameWinPanel");
        
        ResumeButton = GameObject.Find("ResumeButton").GetComponent<Button>();
        MainMenuButton = GameObject.Find("MainMenuButton").GetComponent<Button>();
        RestartButton = GameObject.Find("RestartButton").GetComponent<Button>();
        QuitButton = GameObject.Find("QuitButton").GetComponent<Button>();
        
        _coinText = GameObject.Find("CoinText").GetComponent<TMP_Text>();
        _healthSlider = GameObject.Find("HealthSlider").GetComponent<Slider>();

        
        //初始化ui 
        UpdateCoin();
        UpdateHealth();
    }

    public void UpdateCoin()
    {
        _coinText.text = Player.Instance.coin.ToString();
    }
    public void UpdateHealth()
    {
        _healthSlider.value = Player.Instance.hp / Player.Instance.maxHp; 
    }

    
    
    // Start is called before the first frame update
    void Start()
    {
        ResumeButton.onClick.AddListener(() =>
        {
            ResumeGame();
        });
        
        RestartButton.onClick.AddListener(() =>
        {
            RestartGame();
        });
        
        QuitButton.onClick.AddListener(() =>
        {
            ExitGame();
        });
        
        MainMenuButton.onClick.AddListener(() =>
        {
            GoMainMenu();
        });
        
    }

    // Update is called once per frame
    void Update()
    {
        //如果按了退出
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            //暂停菜单面板
            PauseGame();
        }
    }
    
    //暂停游戏
    public void PauseGame()
    {
        CanvasGroup cg = PausePanel.GetComponent<CanvasGroup>();
        cg.alpha = 1;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        Time.timeScale = 0; 
    }
    
    //继续游戏
    public void ResumeGame()
    {
        CanvasGroup cg = PausePanel.GetComponent<CanvasGroup>();
        cg.alpha = 0;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        Time.timeScale = 1; 
    }
    
    //游戏胜利
    public void Win()
    {
        CanvasGroup cg = GameWinPanel.GetComponent<CanvasGroup>();
        cg.alpha = 1;
        cg.blocksRaycasts = true;
        cg.interactable = true;

        isWin = true;

        StartCoroutine(WaitGoMainMenu());
    }
    
    
    //游戏失败
    public void GameOver()
    {
        CanvasGroup cg = GameOverPanel.GetComponent<CanvasGroup>();
        cg.alpha = 1;
        cg.blocksRaycasts = true;
        cg.interactable = true;


        isFail = true;
        
        StartCoroutine(WaitGoMainMenu());
    }
    
    
    //回主菜单
    public void GoMainMenu()
    {
        SceneManager.LoadScene(0);
    }
    
    //退出游戏
    public void ExitGame()
    {
        Application.Quit();
    }
    
    //重新开始游戏
    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    }


    IEnumerator WaitGoMainMenu()
    {
        float timer = 0;

        while (timer < 4)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        
        GoMainMenu();

    }
    
    
    
}
