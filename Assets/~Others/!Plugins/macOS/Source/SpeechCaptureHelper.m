#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>
#import <Speech/Speech.h>

@interface SpeechCaptureHelper : NSObject

@property (nonatomic, strong) AVAudioEngine *audioEngine;
@property (nonatomic, strong) SFSpeechRecognizer *speechRecognizer;
@property (nonatomic, strong) SFSpeechAudioBufferRecognitionRequest *recognitionRequest;
@property (nonatomic, strong) SFSpeechRecognitionTask *recognitionTask;
@property (nonatomic, copy) NSString *latestTranscript;
@property (nonatomic, copy) NSString *eventFilePath;
@property (nonatomic, copy) NSString *stopFilePath;
@property (nonatomic, assign) BOOL didFinish;
@property (nonatomic, assign) BOOL stopRequested;
@property (nonatomic, assign) BOOL didInstallInputTap;

- (void)run;

@end

@implementation SpeechCaptureHelper

- (instancetype)init
{
    self = [super init];
    if (self == nil)
    {
        return nil;
    }

    _audioEngine = [[AVAudioEngine alloc] init];
    _speechRecognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:@"en-US"]];
    _latestTranscript = @"";
    _didFinish = NO;
    _stopRequested = NO;
    _didInstallInputTap = NO;
    return self;
}

- (void)run
{
    [self configureFromArguments];

    if ([NSProcessInfo.processInfo.arguments containsObject:@"--status"])
    {
        [self printCurrentStatuses];
        return;
    }

    [self startStopFileWatcher];
    [self emit:@"STATUS|authorizing"];

    [self requestMicrophoneAccessWithCompletion:^(BOOL granted) {
        if (!granted)
        {
            [self finishWithError:@"Microphone access was denied in macOS Privacy & Security."];
            return;
        }

        [self requestSpeechAccess];
    }];

    [NSRunLoop.mainRunLoop run];
}

- (void)printCurrentStatuses
{
    [self emit:[NSString stringWithFormat:@"MIC|%@", [self microphoneAuthorizationDescription]]];
    [self emit:[NSString stringWithFormat:@"SPEECH|%@", [self speechAuthorizationDescription]]];
    [self emit:@"EXIT|status"];
}

- (void)requestMicrophoneAccessWithCompletion:(void (^)(BOOL granted))completion
{
    AVAuthorizationStatus status = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio];

    switch (status)
    {
        case AVAuthorizationStatusAuthorized:
        {
            completion(YES);
            break;
        }

        case AVAuthorizationStatusNotDetermined:
        {
            [self emit:@"STATUS|requesting-microphone-access"];
            [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:^(BOOL granted) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    completion(granted);
                });
            }];
            break;
        }

        default:
        {
            completion(NO);
            break;
        }
    }
}

- (void)requestSpeechAccess
{
    SFSpeechRecognizerAuthorizationStatus status = [SFSpeechRecognizer authorizationStatus];

    switch (status)
    {
        case SFSpeechRecognizerAuthorizationStatusAuthorized:
        {
            [self beginRecognition];
            break;
        }

        case SFSpeechRecognizerAuthorizationStatusNotDetermined:
        {
            [self emit:@"STATUS|requesting-speech-access"];
            [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus authorizationStatus) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (authorizationStatus == SFSpeechRecognizerAuthorizationStatusAuthorized)
                    {
                        [self beginRecognition];
                        return;
                    }

                    [self finishWithError:@"Speech recognition access was denied in macOS Privacy & Security."];
                });
            }];
            break;
        }

        default:
        {
            [self finishWithError:@"Speech recognition access was denied in macOS Privacy & Security."];
            break;
        }
    }
}

- (void)beginRecognition
{
    if (self.speechRecognizer == nil)
    {
        [self finishWithError:@"The Mac speech recognizer is unavailable for locale en-US."];
        return;
    }

    if (!self.speechRecognizer.isAvailable)
    {
        [self finishWithError:@"Mac speech recognition is currently unavailable."];
        return;
    }

    [self emit:@"STATUS|starting"];

    [self.recognitionTask cancel];
    self.recognitionTask = nil;
    self.recognitionRequest = [[SFSpeechAudioBufferRecognitionRequest alloc] init];

    if (self.recognitionRequest == nil)
    {
        [self finishWithError:@"The Mac speech recognition request could not be created."];
        return;
    }

    self.recognitionRequest.shouldReportPartialResults = YES;

    if (@available(macOS 13.0, *))
    {
        self.recognitionRequest.addsPunctuation = YES;
    }

    AVAudioInputNode *inputNode = self.audioEngine.inputNode;
    AVAudioFormat *recordingFormat = [inputNode outputFormatForBus:0];

    [inputNode removeTapOnBus:0];

    __weak typeof(self) weakSelf = self;
    [inputNode installTapOnBus:0 bufferSize:1024 format:recordingFormat block:^(AVAudioPCMBuffer *buffer, AVAudioTime *when) {
        [weakSelf.recognitionRequest appendAudioPCMBuffer:buffer];
    }];
    self.didInstallInputTap = YES;

    [self.audioEngine prepare];

    NSError *startError = nil;
    if (![self.audioEngine startAndReturnError:&startError])
    {
        [self finishWithError:[NSString stringWithFormat:@"The Mac microphone could not start: %@", startError.localizedDescription]];
        return;
    }

    self.recognitionTask = [self.speechRecognizer recognitionTaskWithRequest:self.recognitionRequest resultHandler:^(SFSpeechRecognitionResult *result, NSError *error) {
        if (result != nil)
        {
            NSString *transcript = [self normalize:result.bestTranscription.formattedString];
            self.latestTranscript = transcript;
            [self emit:[NSString stringWithFormat:@"TRANSCRIPT|%@", transcript]];

            if (result.isFinal)
            {
                [self finishSuccessfully];
            }
        }

        if (error != nil)
        {
            if (self.stopRequested)
            {
                [self finishSuccessfully];
                return;
            }

            [self finishWithError:[NSString stringWithFormat:@"Mac speech recognition failed: %@", error.localizedDescription]];
        }
    }];

    [self emit:@"STATUS|listening"];
}

- (void)stopRecognition
{
    if (self.stopRequested || self.didFinish)
    {
        return;
    }

    self.stopRequested = YES;
    [self emit:@"STATUS|stopping"];

    [self.audioEngine stop];

    if (self.didInstallInputTap)
    {
        [self.audioEngine.inputNode removeTapOnBus:0];
        self.didInstallInputTap = NO;
    }

    [self.recognitionRequest endAudio];

    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(1.2 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (self.didFinish)
        {
            return;
        }

        [self finishSuccessfully];
    });
}

- (void)configureFromArguments
{
    NSArray<NSString *> *arguments = NSProcessInfo.processInfo.arguments;

    for (NSUInteger index = 0; index < arguments.count; index++)
    {
        NSString *argument = arguments[index];

        if ([argument isEqualToString:@"--event-file"] && index + 1 < arguments.count)
        {
            self.eventFilePath = arguments[index + 1];
            index++;
            continue;
        }

        if ([argument isEqualToString:@"--stop-file"] && index + 1 < arguments.count)
        {
            self.stopFilePath = arguments[index + 1];
            index++;
        }
    }

    if (self.eventFilePath.length > 0)
    {
        [[NSFileManager defaultManager] removeItemAtPath:self.eventFilePath error:nil];
        [[NSData data] writeToFile:self.eventFilePath atomically:YES];
    }

    if (self.stopFilePath.length > 0)
    {
        [[NSFileManager defaultManager] removeItemAtPath:self.stopFilePath error:nil];
    }
}

- (void)startStopFileWatcher
{
    if (self.stopFilePath.length == 0)
    {
        return;
    }

    [NSTimer scheduledTimerWithTimeInterval:0.25
                                     target:self
                                   selector:@selector(checkForStopSignal)
                                   userInfo:nil
                                    repeats:YES];
}

- (void)checkForStopSignal
{
    if (self.stopFilePath.length == 0 || self.didFinish || self.stopRequested)
    {
        return;
    }

    if ([[NSFileManager defaultManager] fileExistsAtPath:self.stopFilePath])
    {
        [self stopRecognition];
    }
}

- (void)finishSuccessfully
{
    if (self.didFinish)
    {
        return;
    }

    self.didFinish = YES;
    [self emit:@"STATUS|stopped"];
    [self emit:[NSString stringWithFormat:@"FINAL|%@", self.latestTranscript]];
    [self cleanupRecognition];
    [self emit:@"EXIT|complete"];
    exit(0);
}

- (void)finishWithError:(NSString *)message
{
    if (self.didFinish)
    {
        return;
    }

    self.didFinish = YES;
    [self cleanupRecognition];
    [self emit:[NSString stringWithFormat:@"ERROR|%@", [self normalize:message]]];
    [self emit:@"EXIT|error"];
    exit(1);
}

- (void)cleanupRecognition
{
    [self.audioEngine stop];

    if (self.didInstallInputTap)
    {
        [self.audioEngine.inputNode removeTapOnBus:0];
        self.didInstallInputTap = NO;
    }

    [self.recognitionRequest endAudio];
    [self.recognitionTask cancel];

    self.recognitionTask = nil;
    self.recognitionRequest = nil;
}

- (void)emit:(NSString *)line
{
    printf("%s\n", line.UTF8String);
    fflush(stdout);

    if (self.eventFilePath.length == 0)
    {
        return;
    }

    NSString *outputLine = [line stringByAppendingString:@"\n"];
    NSFileHandle *handle = [NSFileHandle fileHandleForWritingAtPath:self.eventFilePath];
    if (handle == nil)
    {
        return;
    }

    @try
    {
        [handle seekToEndOfFile];
        [handle writeData:[outputLine dataUsingEncoding:NSUTF8StringEncoding]];
    }
    @catch (NSException *exception)
    {
        // Ignore transient file write failures.
    }
    @finally
    {
        [handle closeFile];
    }
}

- (NSString *)normalize:(NSString *)value
{
    return [[[value ?: @""
        stringByReplacingOccurrencesOfString:@"\n" withString:@" "]
        stringByReplacingOccurrencesOfString:@"\r" withString:@" "]
        stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
}

- (NSString *)microphoneAuthorizationDescription
{
    switch ([AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio])
    {
        case AVAuthorizationStatusAuthorized:
            return @"authorized";
        case AVAuthorizationStatusDenied:
            return @"denied";
        case AVAuthorizationStatusRestricted:
            return @"restricted";
        case AVAuthorizationStatusNotDetermined:
            return @"not-determined";
    }
}

- (NSString *)speechAuthorizationDescription
{
    switch ([SFSpeechRecognizer authorizationStatus])
    {
        case SFSpeechRecognizerAuthorizationStatusAuthorized:
            return @"authorized";
        case SFSpeechRecognizerAuthorizationStatusDenied:
            return @"denied";
        case SFSpeechRecognizerAuthorizationStatusRestricted:
            return @"restricted";
        case SFSpeechRecognizerAuthorizationStatusNotDetermined:
            return @"not-determined";
    }
}

@end

int main(int argc, const char * argv[])
{
    @autoreleasepool
    {
        SpeechCaptureHelper *helper = [[SpeechCaptureHelper alloc] init];
        [helper run];
    }

    return 0;
}
