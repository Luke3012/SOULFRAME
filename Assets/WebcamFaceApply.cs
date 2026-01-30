using UnityEngine;

public class WebcamFaceApply : MonoBehaviour
{
    public SkinnedMeshRenderer targetHead; // Il tuo modello 3D
    private WebCamTexture webcam;
    private Texture2D savedPhoto;

    void Start()
    {
        // 1. Avvia la Webcam
        webcam = new WebCamTexture();
        webcam.Play();
    }

    void Update()
    {
        // 2. Se premi SPAZIO, scatta e applica
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ApplyPhotoToFace();
        }
    }

    void ApplyPhotoToFace()
    {
        // Crea una "foto" statica dai pixel attuali della webcam
        savedPhoto = new Texture2D(webcam.width, webcam.height);
        savedPhoto.SetPixels(webcam.GetPixels());
        savedPhoto.Apply();

        // Trova il materiale della faccia e sostituisci la texture
        // NOTA: Funziona meglio se il modello ha una mappatura UV frontale
        targetHead.material.mainTexture = savedPhoto;

        Debug.Log("Faccia applicata!");

        // (Opzionale) Ferma la webcam per risparmiare risorse
        // webcam.Stop(); 
    }
}