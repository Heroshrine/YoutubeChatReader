import sys
import pyttsx3
import pyautogui

tts_engine = pyttsx3.init()


def speak(text):
    tts_engine.say(text)
    tts_engine.runAndWait()


def simulate_keypress(key):
    pyautogui.hotkey(key)


def main():
    print("Python script initialized")
    sys.stdout.flush()

    while True:
        try:
            commands = sys.stdin.readline().strip().lower().split(":")
            for i in range(len(commands)):
                commands[i] = commands[i].strip().lower()

            if len(commands) == 1:
                print("Commands must be formatted as [COMMAND: PARAMETER]")
                sys.stdout.flush()

            elif commands[0] == "key":
                simulate_keypress(commands[1].split("+"))
                if len(commands) > 2:
                    speak(commands[2])

            elif commands[0] == "wpm":
                tts_engine.setProperty('rate', int(commands[1]))
                print(f"Set tts wpm to: [{commands[1]}]")
                sys.stdout.flush()

            else:
                print("Unknown command")
                sys.stdout.flush()

        except Exception as e:
            print(f"Exception: {e}")
            sys.stdout.flush()
            exit(-1)


if __name__ == "__main__":
    main()
