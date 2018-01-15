# Nest Events Listener
This is a .NET Core app written in C# that demonstrates how to use the Nest Developer API and AWS (Amazon Web Services) to send an SMS when your camera thinks it saw someone.

## Inspiration
My wife requested this =). This works better for us than the normal Nest service notification options (see [FAQ](#faq) for more info).

## Requirements
* Nest Developer account with a custom product that has access to read camera data
* AWS account to send SMS messages (the free tier allows 1 million free publishes)

## Configuration
All configuration is stored in the Secret Manager tool available to .NET Core. This tool is best used during development to keep secrets like authentication outside of your project's settings file to prevent accidental check-in of Production credentials/info.

## Application Flow
* Start a localhost HTTP Listener to act as the Nest OAuth redirect URL
* Load the Works-with-Nest Authentication page in the default web browser
* Upon authorizing, sends the authorization code to the redirect URL
* Gets a Nest OAuth token using product info and the authorization code
* Establishes a REST streaming connection with the Nest service
* When a Nest camera on the account thinks it saw someone, an SMS message is sent out via AWS SNS to multiple phone numbers

<a id="faq"></a>
## FAQ
Q: Doesn't Nest already send notifications?  
A: Yes, the Nest mobile app allows you to control how notifications get sent out, BUT it isn't as fine-grained as we prefer (i.e. receive notifications for selected activity types, or none at all). Besides, this was a fun way to play with the Nest and AWS API =)

Q: Where's all the enterprisey features that encourage flexibility and maintainability like DI and unit tests?  
A: Believe me, I considered it, but this is just a small, personal app that isn't likely to have ongoing development.

## References
* Nest Developer API [https://developers.nest.com/](https://developers.nest.com/)
* AWS - Sending an SMS Message to Multiple Phone Numbers [https://docs.aws.amazon.com/sns/latest/dg/sms_publish-to-topic.html](https://docs.aws.amazon.com/sns/latest/dg/sms_publish-to-topic.html)
* Using the Secret Manager tool in a Console app [Q&A on StackOverflow](https://stackoverflow.com/a/47692741/2141970)
