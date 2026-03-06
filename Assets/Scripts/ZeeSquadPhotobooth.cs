using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Networking;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
public class ZeeSquadPhotobooth : MonoBehaviour
{
    // PANELS
    public GameObject startPanel;
    public GameObject questionPanel;
    public GameObject countdownPanel;
    public GameObject framePanel;
    public GameObject resultPanel;

    // BUTTONS
    public Button startButton;
    public Button[] answerButtons;
    public Button[] frameButtons;
    public Button printButton;
    public Button restartButton;

    // COUNTDOWN
    public Image[] countdownImages;

    // CAMERA
    public RawImage cameraView;
    private WebCamTexture webcam;

    // FRAME
    public Image frameOverlay;
    public Sprite[] frameSprites;
    private int selectedFrame = 0;

    public RawImage qrImage;

    // AUDIO
    public AudioClip beep;
    private AudioSource audioSource;

    // DATA
    private Texture2D lastPhoto;
    private List<string> savedPhotos = new List<string>();


    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();

        SetupButtons();

        StartCoroutine(StartCamera());
        ShowPanel(startPanel);
    }

    void SetupButtons()
    {
        if (startButton)
            startButton.onClick.AddListener(() => ShowPanel(questionPanel));

        for (int i = 0; i < answerButtons.Length; i++)
        {
            answerButtons[i].onClick.AddListener(OnAnswer);
        }

        for (int i = 0; i < frameButtons.Length; i++)
        {
            int id = i;
            frameButtons[i].onClick.AddListener(() => SelectFrame(id));
        }

        if (printButton)
            printButton.onClick.AddListener(PrintPhoto);

        if (restartButton)
            restartButton.onClick.AddListener(RestartBooth);
    }

    void ShowPanel(GameObject panel)
    {
        startPanel.SetActive(false);
        questionPanel.SetActive(false);
        countdownPanel.SetActive(false);
        framePanel.SetActive(false);
        resultPanel.SetActive(false);

        panel.SetActive(true);

        bool enableFrames = (lastPhoto != null && panel == framePanel);
        foreach (var btn in frameButtons)
            btn.interactable = enableFrames;
    }
    // CAMERA
    IEnumerator StartCamera()
    {
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("No camera found");
            yield break;
        }

        WebCamDevice device = WebCamTexture.devices[0];

        webcam = new WebCamTexture(device.name, 1280, 720, 30);

        cameraView.texture = webcam;

        webcam.Play();

        // انتظار الكاميرا
        while (webcam.width < 100)
            yield return null;

        Debug.Log("Camera Ready");
    }

    // ANSWER
    void OnAnswer()
    {
        ShowPanel(countdownPanel);
        StartCoroutine(Countdown());
    }

    IEnumerator Countdown()
    {
        foreach (var img in countdownImages)
            img.gameObject.SetActive(false);

        for (int i = 0; i < 3; i++)
        {
            countdownImages[i].gameObject.SetActive(true);

            if (beep)
                audioSource.PlayOneShot(beep);

            yield return new WaitForSeconds(1);

            countdownImages[i].gameObject.SetActive(false);
        }

        Capture();

        yield return new WaitForSeconds(0.5f);

        ShowPanel(framePanel);
    }

    // CAPTURE
    void Capture()
    {
        if (webcam == null || !webcam.isPlaying)
        {
            Debug.LogError("Camera not running!");
            return;
        }

        Texture2D photo = new Texture2D(webcam.width, webcam.height);

        photo.SetPixels(webcam.GetPixels());
        photo.Apply();

        lastPhoto = photo;

        Debug.Log("Photo Captured");
    }

    // FRAME
    void SelectFrame(int index)
    {
        selectedFrame = index;

        StartCoroutine(ProcessPhoto());
    }

    IEnumerator ProcessPhoto()
    {
        if (lastPhoto == null)
        {
            Debug.LogError("No photo captured yet! Cannot apply frame.");
            yield break;
        }

        Texture2D finalPhoto = ApplyFrame(lastPhoto, selectedFrame);

        SavePhoto(finalPhoto);

        ShowPanel(resultPanel);

        StartCoroutine(UploadPhoto(finalPhoto));

        yield break;
    }

    Texture2D ApplyFrame(Texture2D photo, int frameIndex)
    {
        int targetWidth = 1200;
        int targetHeight = 1800;

        Texture2D resized = new Texture2D(targetWidth, targetHeight);

        // Resize photo
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                float u = (float)x / targetWidth;
                float v = (float)y / targetHeight;

                Color col = photo.GetPixelBilinear(u, v);
                resized.SetPixel(x, y, col);
            }
        }

        resized.Apply();

        if (frameSprites == null || frameSprites.Length == 0)
            return resized;

        Texture2D frame = frameSprites[frameIndex].texture;

        int startX = (targetWidth - frame.width) / 2;
        int startY = (targetHeight - frame.height) / 2;

        for (int y = 0; y < frame.height; y++)
        {
            for (int x = 0; x < frame.width; x++)
            {
                Color c = frame.GetPixel(x, y);

                if (c.a > 0)
                {
                    resized.SetPixel(startX + x, startY + y, c);
                }
            }
        }

        resized.Apply();

        return resized;
    }

    // SAVE
    void SavePhoto(Texture2D photo)
    {
        string name = "Photo_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

        string path = Path.Combine(Application.persistentDataPath, name);

        File.WriteAllBytes(path, photo.EncodeToPNG());

        savedPhotos.Add(path);

        Debug.Log("Saved: " + path);
    }

    // UPLOAD
    IEnumerator UploadPhoto(Texture2D photo)
    {
        byte[] bytes = photo.EncodeToPNG();

        string base64 = System.Convert.ToBase64String(bytes);

        WWWForm form = new WWWForm();

        form.AddField("key", "5090363c5e3c49709fa7be1a2960e69a");
        form.AddField("image", base64);

        UnityWebRequest www = UnityWebRequest.Post("https://api.imgbb.com/1/upload", form);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string url = ParseURL(www.downloadHandler.text);

            StartCoroutine(GenerateQR(url));
        }
        else
        {
            Debug.Log("Upload Failed");
        }
    }

    string ParseURL(string json)
    {
        string key = "\"url\":\"";

        int start = json.IndexOf(key);

        if (start == -1) return "";

        start += key.Length;

        int end = json.IndexOf("\"", start);

        return json.Substring(start, end - start);
    }

    // QR
    IEnumerator GenerateQR(string url)
    {
        string qr = "https://api.qrserver.com/v1/create-qr-code/?size=256x256&data=" + UnityWebRequest.EscapeURL(url);

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(qr);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Texture2D tex = DownloadHandlerTexture.GetContent(www);

            qrImage.texture = tex;
        }
    }

    // PRINT
    void PrintPhoto()
    {
        if (savedPhotos.Count == 0)
        {
            UnityEngine.Debug.LogWarning("No photos to print!");
            return;
        }

        string path = savedPhotos[savedPhotos.Count - 1];

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = path;
            psi.Verb = "print";
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("Print failed: " + ex.Message);
        }
        RestartBooth();
    }


    // RESTART
    void RestartBooth()
    {
        qrImage.texture = null;
        ShowPanel(startPanel);
    }

    void OnDestroy()
    {
        if (webcam)
            webcam.Stop();
    }
}