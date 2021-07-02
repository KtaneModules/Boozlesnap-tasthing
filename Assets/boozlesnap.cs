using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using System.Text.RegularExpressions;
using rnd = UnityEngine.Random;

public class boozlesnap : MonoBehaviour
{
    public new KMAudio audio;
    public KMBombInfo bomb;
    public KMBombModule module;

    public KMSelectable continueButton;
    public KMSelectable[] ejectButtons;
    public Color[] colors;
    public Color matte;
    public GameObject templateCard;
    public Transform cardParent;
    public Transform sortedCardParent;
    public TextMesh colorblindText;

    private Card currentCard;
    private Card previousCard;
    private int currentTurn;
    private int illegalProbability;
    private bool cardIllegal;
    private int ejectModulo;
    private int ejectComparison;
    private bool[] ejectedPlayers = new bool[4];
    private int[] serialNumberValues;

    private bool moduleStarted;
    private bool straighteningCard;
    private int cardsStacked;
    private Queue<CardAnimation> animations = new Queue<CardAnimation>();
    private static readonly string base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly string[] directions = new string[] { "up", "right", "down", "left" };
    private static readonly string[] ordinals = new string[] { "1st", "2nd", "3rd", "4th" };
    private static readonly string[] colorNames = new string[] { "rose", "red", "orange", "yellow", "lime", "green", "jade", "cyan", "azure", "blue", "violet", "magenta", "white", "grey", "black" };
    private static readonly int[][] keyNumberIndices = new int[][]
    {
        new int[] { 2, 3, 4, 6, 1 },
        new int[] { 1, 3, 2, 5, 6 },
        new int[] { 3, 4, 6, 2, 5 },
        new int[] { 1, 4, 6, 5, 3 },
        new int[] { 4, 5, 2, 1, 6 },
        new int[] { 2, 4, 3, 6, 5 },
        new int[] { 6, 5, 4, 3, 1 },
        new int[] { 2, 1, 5, 6, 4 },
        new int[] { 5, 6, 3, 4, 2 },
        new int[] { 6, 1, 2, 5, 4 },
        new int[] { 3, 5, 6, 4, 1 },
        new int[] { 4, 6, 3, 1, 2 },
        new int[] { 6, 2, 1, 3, 5 },
        new int[] { 5, 3, 1, 2, 6 },
        new int[] { 1, 6, 4, 3, 2 }
    };
    private static readonly Vector3[] startingPositions = new Vector3[]
    {
        new Vector3(0f, .0857f, .1833f),
        new Vector3(.1833f, .0857f, 0f),
        new Vector3(0f, .0857f, -.1833f),
        new Vector3(-.1833f, .0857f, 0f)
    };

    private static int moduleIdCounter = 1;
    private int moduleId;
    private bool moduleSolved;

    private void Awake()
    {
        moduleId = moduleIdCounter++;
        continueButton.OnInteract += delegate () { PressContinueButton(); return false; };
        foreach (KMSelectable button in ejectButtons)
            button.OnInteract += delegate () { PressEjectButton(button); return false; };
        colorblindText.gameObject.SetActive(GetComponent<KMColorblindMode>().ColorblindModeActive);
    }

    private void Start()
    {
        StartCoroutine(Animate());
        illegalProbability = 3;
        serialNumberValues = bomb.GetSerialNumber().Select(x => base36.IndexOf(x)).ToArray();
    }

    private void PressContinueButton()
    {
        audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, continueButton.transform);
        continueButton.AddInteractionPunch(.4f);
        if (moduleSolved)
            return;
        if (!moduleStarted)
        {
            BeginModule();
            moduleStarted = true;
        }
        else
        {
            if (!cardIllegal)
            {
                Debug.LogFormat("[Boozlesnap #{0}] Play continues...", moduleId);
                currentTurn = (currentTurn + 1) % 4;
                while (ejectedPlayers[currentTurn])
                    currentTurn = (currentTurn + 1) % 4;
                PlayCard();
            }
            else
            {
                Debug.LogFormat("[Boozlesnap #{0}] You tried to progress the game when an illegal card had been played. Strike!", moduleId);
                module.HandleStrike();
            }
        }
    }

    private void PressEjectButton(KMSelectable button)
    {
        audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, button.transform);
        button.AddInteractionPunch(.4f);
        if (moduleSolved)
            return;
        var ix = Array.IndexOf(ejectButtons, button);
        var submittedTime = ((int)bomb.GetTime()) % 60;
        if (ix != currentTurn)
        {
            Debug.LogFormat("[Boozlesnap #{0}] You tried to eject a player who hasn't just played a card. Strike!", moduleId);
            module.HandleStrike();
        }
        else if (!cardIllegal)
        {
            Debug.LogFormat("[Boozlesnap #{0}] You tried to eject a player when their card was legal. Strike!", moduleId);
            module.HandleStrike();
        }
        else if (submittedTime % ejectModulo != ejectComparison)
        {
            Debug.LogFormat("[Boozlesnap #{0}] You tried to eject a player at an invalid time. Strike!", moduleId);
            module.HandleStrike();
        }
        else
        {
            Debug.LogFormat("[Boozlesnap #{0}] Successfully ejected the {1} player.", moduleId, directions[ix]);
            StartCoroutine(StraightenAllCards());
            ejectedPlayers[ix] = true;
            currentTurn = (currentTurn + 1) % 4;
            while (ejectedPlayers[currentTurn])
                currentTurn = (currentTurn + 1) % 4;
            if (ejectedPlayers.Count(x => !x) == 1)
            {
                Debug.LogFormat("[Boozlesnap #{0}] Every player except one has been ejected. Module solved!", moduleId);
                module.HandlePass();
                moduleSolved = true;
            }
            else
                PlayCard();
        }
    }

    private void BeginModule()
    {
        currentTurn = rnd.Range(0, 4);
        Debug.LogFormat("[Boozlesnap #{0}] Play has started! The {1} player begins.", moduleId, directions[currentTurn]);
        PlayCard(firstTime: true);
    }

    private void PlayCard(bool firstTime = false)
    {
        if (!firstTime)
            previousCard = currentCard;
        var thisCardIllegal = rnd.Range(1, 11) <= illegalProbability;
    tryAgain:
        currentCard = GenerateCard();
        if (firstTime)
        {
            cardIllegal = false;
            Debug.LogFormat("[Boozlesnap #{0}] The first card is legal no matter what. Skipping calculations...", moduleId);
        }
        else
        {
            if (CalculateCardIllegality() != thisCardIllegal)
                goto tryAgain;
            cardIllegal = thisCardIllegal;
        }
        cardsStacked++;
        animations.Enqueue(new CardAnimation(currentCard, currentTurn));
        Debug.LogFormat("[Boozlesnap #{0}] The {1} player played a card with a glyph in group {2}, a color of {3}, and a count of {4}.", moduleId, directions[currentTurn], currentCard.group + 1, colorNames[currentCard.color], currentCard.count + 1);
        Debug.LogFormat("[Boozlesnap #{0}] This card is {1}.", moduleId, cardIllegal ? "illegal" : "legal");
        if (cardIllegal)
            illegalProbability = 3;
        else if (illegalProbability != 9)
            illegalProbability++;
        if (cardIllegal)
        {
            var keyNumber = keyNumberIndices[currentCard.color].Take(currentCard.count + 1).Select(x => serialNumberValues[x - 1]).Sum();
            Debug.LogFormat("[Boozlesnap #{0}] The key number is {1}.", moduleId, keyNumber);
            var moduloCandidates = new int[] { 7, 8, 9, 10 };
            var comparisonCandidates = new int[] { keyNumber % 7, 7 - (keyNumber % 8), keyNumber % 9, 9 - (keyNumber % 10) };
            ejectModulo = moduloCandidates[currentCard.index];
            ejectComparison = comparisonCandidates[currentCard.index];
            Debug.LogFormat("[Boozlesnap #{0}] The card's glyphs are the {1} in its group. (Eject when seconds digits % {2} = {3}).", moduleId, ordinals[currentCard.index], ejectModulo, ejectComparison);
        }
    }

    private bool CalculateCardIllegality()
    {
        if (currentCard.family == previousCard.family)
            return currentCard.group % 2 == previousCard.group % 2;
        else if (currentCard.group == previousCard.group)
            return Math.Abs(currentCard.count - previousCard.count) != 1;
        else if (currentCard.count == previousCard.count)
        {
            if (currentCard.family == 0)
                return previousCard.family != 4 || previousCard.family != 1;
            else if (currentCard.family == 1)
                return previousCard.family != 0 || previousCard.family != 2;
            else if (currentCard.family == 2)
                return previousCard.family != 1 || previousCard.family != 3;
            else if (currentCard.family == 3)
                return previousCard.family != 2 || previousCard.family != 4;
            else
                return previousCard.family != 3 || previousCard.family != 0;
        }
        else
            return true;
    }

    private class Card
    {
        public int group { get; set; }
        public int index { get; set; }
        public int color { get; set; }
        public int family { get; set; }
        public int count { get; set; }

        public Card(int g, int c, int ct)
        {
            group = g;
            index = rnd.Range(0, 4);
            color = c;
            family = c / 3;
            count = ct;
        }
    }

    private Card GenerateCard()
    {
        return new Card(rnd.Range(0, 4), rnd.Range(0, 15), rnd.Range(0, 5));
    }

    private IEnumerator Animate()
    {
        while (true)
        {
            while (animations.Count == 0 || straighteningCard)
                yield return null;
            var animation = animations.Dequeue();
            var thisCard = animation.card;
            var startPosition = startingPositions[animation.player];
            var endPosition = new Vector3(0f, .0156f + .0005f * (float)cardsStacked, 0f);
            var endAngle = rnd.Range(-25f, 25f);
            var newCard = Instantiate(templateCard, cardParent, false);
            var frontFace = newCard.transform.Find("front");
            var cardText = frontFace.GetComponentInChildren<TextMesh>();
            if (thisCard.color == 12)
                frontFace.GetComponent<Renderer>().material.color = matte;
            cardText.color = colors[thisCard.color];
            colorblindText.text = colorNames[thisCard.color].ToUpperInvariant();
            var glyph = "ABDE;GHIK;LMNO;QSXZ".Split(';').ToArray()[thisCard.group][thisCard.index].ToString();
            switch (thisCard.count)
            {
                case 0:
                    cardText.text = glyph;
                    break;
                case 1:
                    cardText.text = string.Format("{0}  \n\n  {0}", glyph);
                    break;
                case 2:
                    cardText.text = string.Format("{0}      \n   {0}   \n      {0}", glyph);
                    break;
                case 3:
                    cardText.text = string.Format("{0}    {0}\n\n{0}    {0}", glyph);
                    break;
                case 4:
                    cardText.text = string.Format("{0}    {0}\n   {0}   \n{0}    {0}", glyph);
                    break;
            }
            newCard.transform.localPosition = startPosition;
            var elapsed = 0f;
            var duration = 1f;
            while (elapsed < duration)
            {
                newCard.transform.localPosition = new Vector3(
                    Easing.OutQuad(elapsed, startPosition.x, endPosition.x, duration),
                    Easing.OutQuad(elapsed, startPosition.y, endPosition.y, duration),
                    Easing.OutQuad(elapsed, startPosition.z, endPosition.z, duration)
                );
                newCard.transform.localEulerAngles = new Vector3(0f, Easing.OutQuad(elapsed, 0f, endAngle, duration), 0f);
                yield return null;
                elapsed += Time.deltaTime;
            }
            newCard.transform.localPosition = endPosition;
            newCard.transform.localEulerAngles = new Vector3(0f, endAngle, 0f);
            audio.PlaySoundAtTransform("card" + rnd.Range(1, 6), newCard.transform);
            if (moduleSolved && animations.Count == 0)
                yield break;
        }
    }

    private class CardAnimation
    {
        public Card card { get; set; }
        public int player { get; set; }

        public CardAnimation(Card c, int p)
        {
            card = c;
            player = p;
        }
    }

    private IEnumerator StraightenAllCards()
    {
        straighteningCard = true;
        foreach (Transform card in cardParent)
        {
            StartCoroutine(StraightenCard(card));
            yield return new WaitForSeconds(.2f);
        }
        foreach (Transform card in cardParent)
            card.SetParent(sortedCardParent);
        straighteningCard = false;
    }

    private IEnumerator StraightenCard(Transform card)
    {
        straighteningCard = true;
        var elapsed = 0f;
        var duration = .25f;
        var start = card.localRotation;
        while (elapsed < duration)
        {
            card.localRotation = Quaternion.Slerp(start, Quaternion.Euler(0f, 0f, 0f), elapsed / duration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        card.localRotation = Quaternion.Euler(0f, 0f, 0f);
        audio.PlaySoundAtTransform("card" + rnd.Range(1, 6), card);
    }

    // Twitch Plays
#pragma warning disable 414
    private readonly string TwitchHelpMessage = "!{0} <continue/next/play> [Presses the continue button.] !{0} <up/right/down/left> ## [Presses that eject button when the seconds digits are ##.]";
#pragma warning restore 414

    private IEnumerator ProcessTwitchCommand(string input)
    {
        var command = input.ToLowerInvariant().Split(' ').ToArray();
        if (command.Length == 1)
        {
            if (command[0] == "continue" || command[0] == "cont" || command[0] == "next" || command[0] == "play")
            {
                yield return null;
                continueButton.OnInteract();
            }
            else
                yield break;
        }
        else if (command.Length == 2)
        {
            var directions = new string[] { "up", "right", "down", "left", "u", "r", "d", "l", "top", "bottom", "north", "east", "south", "west" };
            if (!directions.Contains(command[0]) || !Enumerable.Range(0, 60).Select(x => x.ToString("00")).Contains(command[1]))
                yield break;
            var buttonToPress = 0;
            switch (command[0])
            {
                case "right":
                case "r":
                case "east":
                    buttonToPress = 1;
                    break;
                case "down":
                case "d":
                case "bottom":
                case "south":
                    buttonToPress = 2;
                    break;
                case "left":
                case "l":
                case "west":
                    buttonToPress = 3;
                    break;
            }
            yield return null;
            while ((((int)bomb.GetTime()) % 60).ToString("00") != command[1])
                yield return "trycancel";
            ejectButtons[buttonToPress].OnInteract();
        }
        else
            yield break;
    }

    private IEnumerator TwitchHandleForcedSolve()
    {
        yield return null;
        if (!moduleStarted)
        {
            continueButton.OnInteract();
            yield return null;
        }
        while (!moduleSolved)
        {
            while (animations.Count != 0)
                yield return true;
            if (!cardIllegal)
                continueButton.OnInteract();
            else
            {
                while (((int)bomb.GetTime() % 60) % ejectModulo != ejectComparison)
                    yield return true;
                ejectButtons[currentTurn].OnInteract();
            }
        }
    }
}
